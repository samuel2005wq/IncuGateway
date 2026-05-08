using nanoFramework.Hardware.Esp32;
using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Proyecto
{
    public class MicroSdRawFat32Reader
    {
        private readonly int _spiBusId;
        private readonly int _mosiPin;
        private readonly int _misoPin;
        private readonly int _sckPin;
        private readonly int _csPin;
        private readonly int _spiFrequency;

        private GpioController _gpio;
        private SpiDevice _spi;

        private bool _isSdhc;

        private uint _partitionStartSector;
        private ushort _bytesPerSector;
        private byte _sectorsPerCluster;
        private ushort _reservedSectors;
        private byte _numberOfFats;
        private uint _fatSize;
        private uint _rootCluster;

        private uint _fatStartSector;
        private uint _dataStartSector;

        public MicroSdRawFat32Reader(
            int spiBusId,
            int mosiPin,
            int misoPin,
            int sckPin,
            int csPin,
            int spiFrequency)
        {
            _spiBusId = spiBusId;
            _mosiPin = mosiPin;
            _misoPin = misoPin;
            _sckPin = sckPin;
            _csPin = csPin;
            _spiFrequency = spiFrequency;
        }

        public byte[] ReadBinaryFile(string fileName)
        {
            FatDirectoryEntry entry;

            if (!FindFileByNameInRoot(fileName, out entry))
            {
                Debug.WriteLine("No se encontró el archivo: " + fileName);
                return null;
            }

            Debug.WriteLine("");
            Debug.WriteLine("Archivo encontrado:");
            Debug.WriteLine("Nombre = " + fileName);
            Debug.WriteLine("Primer cluster = " + entry.FirstCluster);
            Debug.WriteLine("Tamaño = " + entry.FileSize + " bytes");

            return ReadFileAsBytes(entry.FirstCluster, entry.FileSize);
        }

        public bool Initialize()
        {
            try
            {
                Debug.WriteLine("=== Inicializando microSD RAW FAT32 ===");

                Configuration.SetPinFunction(_mosiPin, DeviceFunction.SPI1_MOSI);
                Configuration.SetPinFunction(_misoPin, DeviceFunction.SPI1_MISO);
                Configuration.SetPinFunction(_sckPin, DeviceFunction.SPI1_CLOCK);

                Debug.WriteLine("MOSI -> GPIO" + _mosiPin);
                Debug.WriteLine("MISO -> GPIO" + _misoPin);
                Debug.WriteLine("SCK  -> GPIO" + _sckPin);
                Debug.WriteLine("CS   -> GPIO" + _csPin);

                _gpio = new GpioController();
                _gpio.OpenPin(_csPin, PinMode.Output);
                _gpio.Write(_csPin, PinValue.High);

                SpiConnectionSettings settings = new SpiConnectionSettings(_spiBusId, -1)
                {
                    ClockFrequency = _spiFrequency,
                    Mode = SpiMode.Mode0,
                    DataBitLength = 8,
                    Configuration = SpiBusConfiguration.FullDuplex
                };

                _spi = SpiDevice.Create(settings);

                Debug.WriteLine("SPI creado a " + _spiFrequency + " Hz.");

                if (!InitializeCard())
                {
                    Debug.WriteLine("No se pudo inicializar la tarjeta SD.");
                    return false;
                }

                Debug.WriteLine("microSD inicializada correctamente.");

                byte[] sector0 = ReadBlock(0);

                if (sector0 == null)
                {
                    Debug.WriteLine("No se pudo leer el sector 0.");
                    return false;
                }

                _partitionStartSector = GetFirstPartitionSector(sector0);

                if (_partitionStartSector == 0)
                {
                    Debug.WriteLine("No se detectó partición válida en el MBR.");
                    return false;
                }

                Debug.WriteLine("Inicio de partición = " + _partitionStartSector);

                byte[] bootSector = ReadBlock(_partitionStartSector);

                if (bootSector == null)
                {
                    Debug.WriteLine("No se pudo leer el boot sector.");
                    return false;
                }

                if (!ParseBootSector(bootSector))
                {
                    Debug.WriteLine("Boot sector FAT32 inválido.");
                    return false;
                }

                Debug.WriteLine("FAT32 detectado correctamente.");
                Debug.WriteLine("fatStartSector = " + _fatStartSector);
                Debug.WriteLine("dataStartSector = " + _dataStartSector);
                Debug.WriteLine("rootCluster = " + _rootCluster);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ERROR inicializando microSD:");
                Debug.WriteLine(ex.GetType().FullName);
                Debug.WriteLine(ex.Message);
                Debug.WriteLine(ex.StackTrace);
                return false;
            }
        }

        public string ReadTextFile(string fileName)
        {
            FatDirectoryEntry entry;

            if (!FindFileByNameInRoot(fileName, out entry))
            {
                Debug.WriteLine("No se encontró el archivo: " + fileName);
                return null;
            }

            Debug.WriteLine("");
            Debug.WriteLine("Archivo encontrado:");
            Debug.WriteLine("Nombre = " + fileName);
            Debug.WriteLine("Primer cluster = " + entry.FirstCluster);
            Debug.WriteLine("Tamaño = " + entry.FileSize + " bytes");

            return ReadFileAsText(entry.FirstCluster, entry.FileSize);
        }

        public bool WriteTextFile(string fileName, string content)
        {
            FatDirectoryEntry entry;

            if (!FindFileByNameInRoot(fileName, out entry))
            {
                Debug.WriteLine("No se encontró el archivo para escribir: " + fileName);
                return false;
            }

            byte[] data = StringToAsciiBytes(content);
            uint capacity = GetClusterChainCapacity(entry.FirstCluster);

            Debug.WriteLine("");
            Debug.WriteLine("Archivo encontrado para escritura:");
            Debug.WriteLine("Nombre = " + fileName);
            Debug.WriteLine("Primer cluster = " + entry.FirstCluster);
            Debug.WriteLine("Tamaño anterior = " + entry.FileSize + " bytes");
            Debug.WriteLine("Tamaño nuevo = " + data.Length + " bytes");
            Debug.WriteLine("Capacidad asignada = " + capacity + " bytes");

            if ((uint)data.Length > capacity)
            {
                Debug.WriteLine("ERROR: el contenido nuevo es más grande que la cadena de clusters existente.");
                Debug.WriteLine("Por ahora este lector no crea nuevos clusters.");
                return false;
            }

            if (!WriteDataToExistingClusterChain(entry.FirstCluster, data))
            {
                Debug.WriteLine("ERROR escribiendo datos en los clusters.");
                return false;
            }

            if (!UpdateFileSize(entry.DirectorySector, entry.DirectoryOffset, (uint)data.Length))
            {
                Debug.WriteLine("ERROR actualizando el tamaño del archivo.");
                return false;
            }

            Debug.WriteLine("Archivo escrito correctamente: " + fileName);
            return true;
        }

        public bool FileExists(string fileName)
        {
            FatDirectoryEntry entry;
            return FindFileByNameInRoot(fileName, out entry);
        }

        // ============================================================
        // Inicialización SD
        // ============================================================

        private bool InitializeCard()
        {
            SendInitialClocks();

            byte r1;

            r1 = SendCommand(0, 0x00000000, 0x95, false);
            Debug.WriteLine("CMD0 R1 = 0x" + r1.ToString("X2"));

            if (r1 != 0x01)
            {
                return false;
            }

            r1 = SendCommand(8, 0x000001AA, 0x87, true);
            Debug.WriteLine("CMD8 R1 = 0x" + r1.ToString("X2"));

            byte[] r7 = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                r7[i] = TransferByte(0xFF);
            }

            DeselectCard();

            Debug.WriteLine("CMD8 R7 = "
                + r7[0].ToString("X2") + " "
                + r7[1].ToString("X2") + " "
                + r7[2].ToString("X2") + " "
                + r7[3].ToString("X2"));

            for (int i = 0; i < 100; i++)
            {
                SendCommand(55, 0x00000000, 0x65, false);
                r1 = SendCommand(41, 0x40000000, 0x77, false);

                Debug.WriteLine("ACMD41 intento " + i + " R1 = 0x" + r1.ToString("X2"));

                if (r1 == 0x00)
                {
                    break;
                }

                Thread.Sleep(50);
            }

            if (r1 != 0x00)
            {
                return false;
            }

            r1 = SendCommand(58, 0x00000000, 0xFD, true);
            Debug.WriteLine("CMD58 R1 = 0x" + r1.ToString("X2"));

            byte[] ocr = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                ocr[i] = TransferByte(0xFF);
            }

            DeselectCard();

            Debug.WriteLine("OCR = "
                + ocr[0].ToString("X2") + " "
                + ocr[1].ToString("X2") + " "
                + ocr[2].ToString("X2") + " "
                + ocr[3].ToString("X2"));

            if ((ocr[0] & 0x40) != 0)
            {
                _isSdhc = true;
                Debug.WriteLine("Tipo probable: SDHC/SDXC.");
            }
            else
            {
                _isSdhc = false;
                Debug.WriteLine("Tipo probable: SDSC.");

                byte r16 = SendCommand(16, 512, 0x15, false);
                Debug.WriteLine("CMD16 R1 = 0x" + r16.ToString("X2"));
            }

            return true;
        }

        // ============================================================
        // FAT32
        // ============================================================

        private bool ParseBootSector(byte[] sector)
        {
            ushort signature = ReadUInt16(sector, 510);

            if (signature != 0xAA55)
            {
                return false;
            }

            _bytesPerSector = ReadUInt16(sector, 11);
            _sectorsPerCluster = sector[13];
            _reservedSectors = ReadUInt16(sector, 14);
            _numberOfFats = sector[16];
            _fatSize = ReadUInt32(sector, 36);
            _rootCluster = ReadUInt32(sector, 44);

            _fatStartSector = _partitionStartSector + _reservedSectors;
            _dataStartSector = _partitionStartSector + _reservedSectors + ((uint)_numberOfFats * _fatSize);

            Debug.WriteLine("Bytes por sector = " + _bytesPerSector);
            Debug.WriteLine("Sectores por cluster = " + _sectorsPerCluster);
            Debug.WriteLine("Sectores reservados = " + _reservedSectors);
            Debug.WriteLine("Número de FATs = " + _numberOfFats);
            Debug.WriteLine("Tamaño FAT = " + _fatSize);
            Debug.WriteLine("Root cluster = " + _rootCluster);

            return _bytesPerSector == 512 && _sectorsPerCluster > 0;
        }

        private bool FindFileByNameInRoot(string fileName, out FatDirectoryEntry result)
        {
            result = new FatDirectoryEntry();

            string target = NormalizeFileName(fileName);
            uint currentCluster = _rootCluster;

            while (currentCluster < 0x0FFFFFF8)
            {
                uint firstSector = ClusterToSector(currentCluster);
                string pendingLongName = "";

                for (uint s = 0; s < _sectorsPerCluster; s++)
                {
                    byte[] sector = ReadBlock(firstSector + s);

                    if (sector == null)
                    {
                        return false;
                    }

                    for (int offset = 0; offset < 512; offset += 32)
                    {
                        byte firstByte = sector[offset];

                        if (firstByte == 0x00)
                        {
                            return false;
                        }

                        if (firstByte == 0xE5)
                        {
                            pendingLongName = "";
                            continue;
                        }

                        byte attributes = sector[offset + 11];

                        if (attributes == 0x0F)
                        {
                            string part = ReadLongFileNamePart(sector, offset);
                            pendingLongName = part + pendingLongName;
                            continue;
                        }

                        if ((attributes & 0x08) != 0)
                        {
                            pendingLongName = "";
                            continue;
                        }

                        if ((attributes & 0x10) != 0)
                        {
                            pendingLongName = "";
                            continue;
                        }

                        string shortName = GetShortFileName(sector, offset);

                        string finalName;

                        if (pendingLongName.Length > 0)
                        {
                            finalName = pendingLongName;
                        }
                        else
                        {
                            finalName = shortName;
                        }

                        Debug.WriteLine("Archivo detectado: [" + finalName + "]");

                        if (NormalizeFileName(finalName) == target)
                        {
                            ushort highCluster = ReadUInt16(sector, offset + 20);
                            ushort lowCluster = ReadUInt16(sector, offset + 26);

                            uint firstCluster = (uint)((highCluster << 16) | lowCluster);
                            uint fileSize = ReadUInt32(sector, offset + 28);

                            result.FirstCluster = firstCluster;
                            result.FileSize = fileSize;
                            result.DirectorySector = firstSector + s;
                            result.DirectoryOffset = offset;

                            return true;
                        }

                        pendingLongName = "";
                    }
                }

                currentCluster = ReadFatEntry(currentCluster);
            }

            return false;
        }

        private byte[] ReadFileAsBytes(uint firstCluster, uint fileSize)
        {
            byte[] result = new byte[(int)fileSize];

            uint remaining = fileSize;
            uint currentCluster = firstCluster;
            int resultIndex = 0;

            while (currentCluster < 0x0FFFFFF8 && remaining > 0)
            {
                uint firstSector = ClusterToSector(currentCluster);

                for (uint s = 0; s < _sectorsPerCluster && remaining > 0; s++)
                {
                    byte[] sector = ReadBlock(firstSector + s);

                    if (sector == null)
                    {
                        return result;
                    }

                    int bytesToRead = remaining >= 512 ? 512 : (int)remaining;

                    for (int i = 0; i < bytesToRead; i++)
                    {
                        result[resultIndex] = sector[i];
                        resultIndex++;
                    }

                    remaining -= (uint)bytesToRead;
                }

                if (remaining > 0)
                {
                    currentCluster = ReadFatEntry(currentCluster);
                }
            }

            return result;
        }

        private string ReadFileAsText(uint firstCluster, uint fileSize)
        {
            string text = "";
            uint remaining = fileSize;
            uint currentCluster = firstCluster;

            while (currentCluster < 0x0FFFFFF8 && remaining > 0)
            {
                uint firstSector = ClusterToSector(currentCluster);

                for (uint s = 0; s < _sectorsPerCluster && remaining > 0; s++)
                {
                    byte[] sector = ReadBlock(firstSector + s);

                    if (sector == null)
                    {
                        return text;
                    }

                    int bytesToRead = remaining >= 512 ? 512 : (int)remaining;

                    for (int i = 0; i < bytesToRead; i++)
                    {
                        byte b = sector[i];

                        if (b == 0x00)
                        {
                            continue;
                        }

                        text += (char)b;
                    }

                    remaining -= (uint)bytesToRead;
                }

                if (remaining > 0)
                {
                    currentCluster = ReadFatEntry(currentCluster);
                }
            }

            return text;
        }

        private bool WriteDataToExistingClusterChain(uint firstCluster, byte[] data)
        {
            uint currentCluster = firstCluster;
            int dataIndex = 0;
            int totalLength = data.Length;

            while (currentCluster < 0x0FFFFFF8)
            {
                uint firstSector = ClusterToSector(currentCluster);

                for (uint s = 0; s < _sectorsPerCluster; s++)
                {
                    byte[] sectorBuffer = new byte[512];

                    for (int i = 0; i < 512; i++)
                    {
                        if (dataIndex < totalLength)
                        {
                            sectorBuffer[i] = data[dataIndex];
                            dataIndex++;
                        }
                        else
                        {
                            sectorBuffer[i] = 0x00;
                        }
                    }

                    if (!WriteBlock(firstSector + s, sectorBuffer))
                    {
                        return false;
                    }

                    if (dataIndex >= totalLength)
                    {
                        return true;
                    }
                }

                currentCluster = ReadFatEntry(currentCluster);
            }

            return dataIndex >= totalLength;
        }

        private uint GetClusterChainCapacity(uint firstCluster)
        {
            uint capacity = 0;
            uint currentCluster = firstCluster;

            while (currentCluster < 0x0FFFFFF8)
            {
                capacity += (uint)_sectorsPerCluster * 512;
                currentCluster = ReadFatEntry(currentCluster);
            }

            return capacity;
        }

        private bool UpdateFileSize(uint directorySector, int directoryOffset, uint newSize)
        {
            byte[] sector = ReadBlock(directorySector);

            if (sector == null)
            {
                return false;
            }

            WriteUInt32(sector, directoryOffset + 28, newSize);

            return WriteBlock(directorySector, sector);
        }

        private uint ReadFatEntry(uint cluster)
        {
            uint fatOffset = cluster * 4;
            uint fatSector = _fatStartSector + (fatOffset / 512);
            int entryOffset = (int)(fatOffset % 512);

            byte[] sector = ReadBlock(fatSector);

            if (sector == null)
            {
                return 0x0FFFFFFF;
            }

            uint value = ReadUInt32(sector, entryOffset);
            return value & 0x0FFFFFFF;
        }

        private uint ClusterToSector(uint cluster)
        {
            return _dataStartSector + ((cluster - 2) * _sectorsPerCluster);
        }

        // ============================================================
        // SD RAW - lectura y escritura de bloques
        // ============================================================

        private byte[] ReadBlock(uint blockNumber)
        {
            uint address = _isSdhc ? blockNumber : blockNumber * 512;
            byte r1 = SendCommandKeepSelected(17, address, 0xFF);

            if (r1 != 0x00)
            {
                DeselectCard();
                return null;
            }

            byte token = 0xFF;

            for (int i = 0; i < 10000; i++)
            {
                token = TransferByte(0xFF);

                if (token == 0xFE)
                {
                    break;
                }
            }

            if (token != 0xFE)
            {
                DeselectCard();
                return null;
            }

            byte[] buffer = new byte[512];

            for (int i = 0; i < 512; i++)
            {
                buffer[i] = TransferByte(0xFF);
            }

            TransferByte(0xFF);
            TransferByte(0xFF);

            DeselectCard();

            return buffer;
        }

        private bool WriteBlock(uint blockNumber, byte[] buffer)
        {
            if (buffer == null || buffer.Length != 512)
            {
                Debug.WriteLine("WriteBlock requiere exactamente 512 bytes.");
                return false;
            }

            uint address = _isSdhc ? blockNumber : blockNumber * 512;
            byte r1 = SendCommandKeepSelected(24, address, 0xFF);

            if (r1 != 0x00)
            {
                DeselectCard();
                Debug.WriteLine("CMD24 falló. R1 = 0x" + r1.ToString("X2"));
                return false;
            }

            TransferByte(0xFF);
            TransferByte(0xFE);

            _spi.Write(buffer);

            TransferByte(0xFF);
            TransferByte(0xFF);

            byte dataResponse = TransferByte(0xFF);

            if ((dataResponse & 0x1F) != 0x05)
            {
                DeselectCard();
                Debug.WriteLine("Respuesta de escritura inválida: 0x" + dataResponse.ToString("X2"));
                return false;
            }

            byte busy;

            do
            {
                busy = TransferByte(0xFF);
            }
            while (busy == 0x00);

            DeselectCard();

            return true;
        }

        private void SendInitialClocks()
        {
            _gpio.Write(_csPin, PinValue.High);

            for (int i = 0; i < 10; i++)
            {
                TransferByte(0xFF);
            }
        }

        private byte SendCommand(byte cmd, uint arg, byte crc, bool keepSelected)
        {
            byte response = SendCommandKeepSelected(cmd, arg, crc);

            if (!keepSelected)
            {
                DeselectCard();
            }

            return response;
        }

        private byte SendCommandKeepSelected(byte cmd, uint arg, byte crc)
        {
            byte[] packet = new byte[6];

            packet[0] = (byte)(0x40 | cmd);
            packet[1] = (byte)((arg >> 24) & 0xFF);
            packet[2] = (byte)((arg >> 16) & 0xFF);
            packet[3] = (byte)((arg >> 8) & 0xFF);
            packet[4] = (byte)(arg & 0xFF);
            packet[5] = crc;

            _gpio.Write(_csPin, PinValue.Low);

            TransferByte(0xFF);
            _spi.Write(packet);

            byte response = 0xFF;

            for (int i = 0; i < 20; i++)
            {
                response = TransferByte(0xFF);

                if (response != 0xFF)
                {
                    break;
                }
            }

            return response;
        }

        private void DeselectCard()
        {
            _gpio.Write(_csPin, PinValue.High);
            TransferByte(0xFF);
        }

        private byte TransferByte(byte value)
        {
            byte[] tx = new byte[] { value };
            byte[] rx = new byte[1];

            _spi.TransferFullDuplex(tx, rx);

            return rx[0];
        }

        // ============================================================
        // Long File Name
        // ============================================================

        private string ReadLongFileNamePart(byte[] sector, int offset)
        {
            string result = "";

            int[] positions = new int[]
            {
                1, 3, 5, 7, 9,
                14, 16, 18, 20, 22, 24,
                28, 30
            };

            for (int i = 0; i < positions.Length; i++)
            {
                int pos = offset + positions[i];
                ushort unicodeChar = ReadUInt16(sector, pos);

                if (unicodeChar == 0x0000 || unicodeChar == 0xFFFF)
                {
                    continue;
                }

                result += (char)unicodeChar;
            }

            return result;
        }

        private string GetShortFileName(byte[] sector, int offset)
        {
            string name = ReadAsciiRaw(sector, offset, 8);
            string ext = ReadAsciiRaw(sector, offset + 8, 3);

            name = TrimSpaces(name);
            ext = TrimSpaces(ext);

            if (ext.Length > 0)
            {
                return name + "." + ext;
            }

            return name;
        }

        private string NormalizeFileName(string text)
        {
            string result = "";

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c != ' ')
                {
                    result += c;
                }
            }

            return result.ToUpper();
        }

        private string TrimSpaces(string text)
        {
            string result = "";

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                {
                    result += text[i];
                }
            }

            return result;
        }

        // ============================================================
        // Utilidades
        // ============================================================

        private uint GetFirstPartitionSector(byte[] sector)
        {
            ushort signature = ReadUInt16(sector, 510);

            if (signature != 0xAA55)
            {
                return 0;
            }

            byte partitionType = sector[450];

            if (partitionType == 0x00)
            {
                return 0;
            }

            return ReadUInt32(sector, 454);
        }

        private ushort ReadUInt16(byte[] data, int index)
        {
            return (ushort)(data[index] | (data[index + 1] << 8));
        }

        private uint ReadUInt32(byte[] data, int index)
        {
            return (uint)(
                data[index]
                | (data[index + 1] << 8)
                | (data[index + 2] << 16)
                | (data[index + 3] << 24));
        }

        private void WriteUInt32(byte[] data, int index, uint value)
        {
            data[index] = (byte)(value & 0xFF);
            data[index + 1] = (byte)((value >> 8) & 0xFF);
            data[index + 2] = (byte)((value >> 16) & 0xFF);
            data[index + 3] = (byte)((value >> 24) & 0xFF);
        }

        private string ReadAsciiRaw(byte[] data, int index, int length)
        {
            string result = "";

            for (int i = 0; i < length; i++)
            {
                result += (char)data[index + i];
            }

            return result;
        }

        private byte[] StringToAsciiBytes(string text)
        {
            byte[] data = new byte[text.Length];

            for (int i = 0; i < text.Length; i++)
            {
                data[i] = (byte)text[i];
            }

            return data;
        }

        private struct FatDirectoryEntry
        {
            public uint FirstCluster;
            public uint FileSize;
            public uint DirectorySector;
            public int DirectoryOffset;
        }

        public bool SendFileToStream(string fileName, Stream outputStream)
        {
            FatDirectoryEntry entry;

            if (!FindFileByNameInRoot(fileName, out entry))
            {
                Debug.WriteLine("No se encontró el archivo: " + fileName);
                return false;
            }

            Debug.WriteLine("");
            Debug.WriteLine("Enviando archivo por bloques:");
            Debug.WriteLine("Nombre = " + fileName);
            Debug.WriteLine("Primer cluster = " + entry.FirstCluster);
            Debug.WriteLine("Tamaño = " + entry.FileSize + " bytes");

            uint remaining = entry.FileSize;
            uint currentCluster = entry.FirstCluster;

            while (currentCluster < 0x0FFFFFF8 && remaining > 0)
            {
                uint firstSector = ClusterToSector(currentCluster);

                for (uint s = 0; s < _sectorsPerCluster && remaining > 0; s++)
                {
                    byte[] sector = ReadBlock(firstSector + s);

                    if (sector == null)
                    {
                        return false;
                    }

                    int bytesToWrite = remaining >= 512 ? 512 : (int)remaining;

                    outputStream.Write(sector, 0, bytesToWrite);

                    remaining -= (uint)bytesToWrite;
                }

                if (remaining > 0)
                {
                    currentCluster = ReadFatEntry(currentCluster);
                }
            }

            outputStream.Flush();

            return true;
        }

    }
}