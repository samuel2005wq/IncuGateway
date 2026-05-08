using System;
using System.IO;

namespace Proyecto
{
    internal class SdConfigHelper
    {
        // ============================================================
        // Configuración SPI para lector RAW FAT32
        // ============================================================

        private const int SpiBusId = 1;

        private const int SdMosiPin = 23;
        private const int SdMisoPin = 19;
        private const int SdSckPin = 18;
        private const int SdCsPin = 27;

        // Frecuencia baja/segura para el HW-125
        private const int SpiFrequency = 1000000;

        private static MicroSdRawFat32Reader _sd;
        private static bool _mounted = false;

        // ============================================================
        // Archivos esperados en la raíz de la microSD
        // ============================================================

        public const string IndexPath = "index.html";

        public const string WifiConfigPath = "WifiCfg.txt";
        public const string MqttConfigPath = "MqttCfg.txt";
        public const string ModbusConfigPath = "ModbusCfg.txt";
        public const string RegisterConfigPath = "RegisterCfg.txt";

        public static bool IsMounted()
        {
            return _mounted;
        }

        public static bool Mount()
        {
            try
            {
                Console.WriteLine("[SD RAW] Inicializando microSD RAW FAT32...");
                Console.WriteLine("[SD RAW] MOSI=" + SdMosiPin.ToString() +
                                  " MISO=" + SdMisoPin.ToString() +
                                  " SCK=" + SdSckPin.ToString() +
                                  " CS=" + SdCsPin.ToString() +
                                  " Freq=" + SpiFrequency.ToString());

                _sd = new MicroSdRawFat32Reader(
                    spiBusId: SpiBusId,
                    mosiPin: SdMosiPin,
                    misoPin: SdMisoPin,
                    sckPin: SdSckPin,
                    csPin: SdCsPin,
                    spiFrequency: SpiFrequency);

                _mounted = _sd.Initialize();

                if (_mounted)
                {
                    Console.WriteLine("[SD RAW] microSD inicializada correctamente");
                    return true;
                }

                Console.WriteLine("[SD RAW] No se pudo inicializar la microSD");
                return false;
            }
            catch (Exception ex)
            {
                _mounted = false;
                Console.WriteLine("[SD RAW] Error Mount(): " + ex.Message);
                return false;
            }
        }

        public static void EnsureFiles()
        {
            if (!_mounted)
            {
                Console.WriteLine("[SD RAW] EnsureFiles omitido: SD no inicializada");
                return;
            }

            Console.WriteLine("[SD RAW] Verificando archivos esperados:");

            CheckFile(WifiConfigPath);
            CheckFile(MqttConfigPath);
            CheckFile(ModbusConfigPath);
            CheckFile(RegisterConfigPath);
            CheckFile(IndexPath);
        }

        private static void CheckFile(string fileName)
        {
            try
            {
                if (_sd.FileExists(NormalizeFileName(fileName)))
                {
                    Console.WriteLine("[SD RAW] OK: " + fileName);
                }
                else
                {
                    Console.WriteLine("[SD RAW] No encontrado: " + fileName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SD RAW] Error verificando " + fileName + ": " + ex.Message);
            }
        }

        public static string GetValue(string filePath, string key)
        {
            if (!_mounted)
            {
                return string.Empty;
            }

            try
            {
                string content = ReadAllText(filePath);

                if (content == string.Empty)
                {
                    return string.Empty;
                }

                string[] lines = content.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();

                    if (line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }

                    int eqIndex = line.IndexOf('=');

                    if (eqIndex <= 0)
                    {
                        continue;
                    }

                    string currentKey = line.Substring(0, eqIndex).Trim();
                    string value = line.Substring(eqIndex + 1).Trim();

                    if (currentKey == key)
                    {
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SD RAW] Error leyendo key " + key + ": " + ex.Message);
            }

            return string.Empty;
        }

        public static string ReadAllText(string filePath)
        {
            if (!_mounted)
            {
                return string.Empty;
            }

            try
            {
                string fileName = NormalizeFileName(filePath);

                string text = _sd.ReadTextFile(fileName);

                if (text == null)
                {
                    return string.Empty;
                }

                return text;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SD RAW] Error ReadAllText(" + filePath + "): " + ex.Message);
                return string.Empty;
            }
        }

        public static byte[] ReadAllBytes(string filePath)
        {
            if (!_mounted)
            {
                return null;
            }

            try
            {
                string fileName = NormalizeFileName(filePath);

                return _sd.ReadBinaryFile(fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SD RAW] Error ReadAllBytes(" + filePath + "): " + ex.Message);
                return null;
            }
        }

        public static bool WriteAllText(string filePath, string data)
        {
            if (!_mounted)
            {
                Console.WriteLine("[SD RAW] No se puede guardar. SD no inicializada.");
                return false;
            }

            try
            {
                string fileName = NormalizeFileName(filePath);

                bool ok = _sd.WriteTextFile(fileName, data);

                if (ok)
                {
                    Console.WriteLine("[SD RAW] Archivo guardado: " + fileName);
                }
                else
                {
                    Console.WriteLine("[SD RAW] No se pudo guardar: " + fileName);
                }

                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SD RAW] Error WriteAllText(" + filePath + "): " + ex.Message);
                return false;
            }
        }

        private static string NormalizeFileName(string path)
        {
            if (path == null || path == string.Empty)
            {
                return string.Empty;
            }

            int lastSlash = -1;

            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];

                if (c == '\\' || c == '/')
                {
                    lastSlash = i;
                }
            }

            if (lastSlash >= 0 && lastSlash < path.Length - 1)
            {
                return path.Substring(lastSlash + 1);
            }

            return path;
        }

        public static bool SendFileToStream(string filePath, Stream outputStream)
        {
            if (!_mounted)
            {
                Console.WriteLine("[SD RAW] No se puede enviar archivo. SD no inicializada.");
                return false;
            }

            try
            {
                string fileName = NormalizeFileName(filePath);

                return _sd.SendFileToStream(fileName, outputStream);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SD RAW] Error SendFileToStream(" + filePath + "): " + ex.Message);
                return false;
            }
        }

    }
}