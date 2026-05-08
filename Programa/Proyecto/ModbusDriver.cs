using Iot.Device.Modbus.Client;
using nanoFramework.Hardware.Esp32;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace Proyecto.Modbus
{
    public class ModbusDriver
    {
        #region Atributos

        private ModbusClient _modbusClient;
        private SerialPort _serialPort;

        public int _baudRate;
        public int _dataBits = 8;
        private Parity _parity;
        private StopBits _stopBits;

        public int _TX;
        public int _RX;

        // Bloqueo para evitar que MQTT y HTTP usen Modbus al mismo tiempo.
        private readonly object _modbusLock = new object();

        // Cache de la última lectura válida.
        private short[] _lastGoodData = null;
        private ushort _lastGoodStartAddress = 0;
        private ushort _lastGoodQuantity = 0;

        #endregion

        #region Constructor

        public ModbusDriver(
            int baudRate,
            int dataBits,
            Parity parity,
            StopBits stopBits,
            int tx,
            int rx)
        {
            _baudRate = baudRate;
            _dataBits = dataBits;
            _parity = parity;
            _stopBits = stopBits;
            _TX = tx;
            _RX = rx;
        }

        #endregion

        #region Inicialización

        public bool Initialize()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    Debug.WriteLine("Puerto ya abierto, reutilizando.");
                    return true;
                }

                Configuration.SetPinFunction(_TX, DeviceFunction.COM2_TX);
                Configuration.SetPinFunction(_RX, DeviceFunction.COM2_RX);

                _serialPort = new SerialPort(
                    "COM2",
                    _baudRate,
                    _parity,
                    _dataBits,
                    _stopBits);

                _serialPort.ReadTimeout = 2500;
                _serialPort.WriteTimeout = 1000;
                _serialPort.Open();

                _modbusClient = new ModbusClient(
                    _serialPort,
                    SerialMode.Normal);

                Debug.WriteLine("Modbus inicializado correctamente en COM2");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error configurando el puerto Serial: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Lectura Modbus

        public short[] ReadRegisters(
            byte slaveId,
            ushort startAddress,
            ushort registersQuantity)
        {
            lock (_modbusLock)
            {
                return ReadRegistersInternal(
                    slaveId,
                    startAddress,
                    registersQuantity);
            }
        }

        private short[] ReadRegistersInternal(
            byte slaveId,
            ushort startAddress,
            ushort registersQuantity)
        {
            try
            {
                if (_modbusClient == null)
                {
                    Debug.WriteLine("[Modbus] Cliente no inicializado.");
                    return null;
                }

                short[] readValues = _modbusClient.ReadHoldingRegisters(
                    slaveId,
                    startAddress,
                    registersQuantity);

                if (readValues == null)
                {
                    Debug.WriteLine("Lectura fallida: sin respuesta del esclavo");
                    return null;
                }

                Debug.WriteLine("Lectura exitosa: " + readValues.Length.ToString() + " registros");

                for (int i = 0; i < readValues.Length; i++)
                {
                    Debug.WriteLine("Registro " + (startAddress + i).ToString() + ": " + readValues[i].ToString());
                }

                return readValues;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ERROR tipo: " + ex.GetType().Name);
                Debug.WriteLine("ERROR mensaje: " + ex.Message);
                Debug.WriteLine("ERROR stack: " + ex.StackTrace);
                return null;
            }
        }

        public short[] ReadRegistersFlexible(
            byte slaveId,
            ushort startAddress,
            ushort requestedQuantity)
        {
            lock (_modbusLock)
            {
                if (requestedQuantity == 0)
                {
                    return null;
                }

                // 1. Si ya existe una cantidad estable, probar esa primero.
                if (_lastGoodQuantity > 0)
                {
                    Console.WriteLine(
                        "[Modbus] Leyendo cantidad estable: " +
                        _lastGoodQuantity.ToString() +
                        " registros desde " +
                        startAddress.ToString());

                    short[] stableData = ReadRegistersInternal(
                        slaveId,
                        startAddress,
                        _lastGoodQuantity);

                    if (stableData != null && stableData.Length > 0)
                    {
                        SaveLastData(stableData, startAddress);

                        Console.WriteLine(
                            "[Modbus] Lectura estable OK: " +
                            stableData.Length.ToString() +
                            " registros");

                        return stableData;
                    }

                    Console.WriteLine("[Modbus] Falló la cantidad estable. Recalculando...");
                    _lastGoodQuantity = 0;
                }

                // 2. Si no existe cantidad estable, buscar desde la cantidad pedida hacia abajo.
                for (ushort q = requestedQuantity; q >= 1; q--)
                {
                    Console.WriteLine(
                        "[Modbus] Intentando leer " +
                        q.ToString() +
                        " registros desde " +
                        startAddress.ToString());

                    short[] data = ReadRegistersInternal(
                        slaveId,
                        startAddress,
                        q);

                    if (data != null && data.Length > 0)
                    {
                        SaveLastData(data, startAddress);

                        Console.WriteLine(
                            "[Modbus] Lectura flexible OK: " +
                            data.Length.ToString() +
                            " registros");

                        return data;
                    }

                    Thread.Sleep(50);

                    if (q == 1)
                    {
                        break;
                    }
                }

                Console.WriteLine("[Modbus] No se pudo leer ningun registro.");

                // Si falla una lectura nueva, entregamos la última lectura buena si existe.
                if (_lastGoodData != null && _lastGoodData.Length > 0)
                {
                    Console.WriteLine("[Modbus] Usando última lectura válida en cache.");
                    return _lastGoodData;
                }

                return null;
            }
        }

        private void SaveLastData(short[] data, ushort startAddress)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            _lastGoodData = data;
            _lastGoodStartAddress = startAddress;
            _lastGoodQuantity = (ushort)data.Length;
        }

        public short[] GetLastGoodData()
        {
            lock (_modbusLock)
            {
                if (_lastGoodData == null)
                {
                    return null;
                }

                short[] copy = new short[_lastGoodData.Length];

                for (int i = 0; i < _lastGoodData.Length; i++)
                {
                    copy[i] = _lastGoodData[i];
                }

                return copy;
            }
        }

        public ushort GetLastGoodStartAddress()
        {
            lock (_modbusLock)
            {
                return _lastGoodStartAddress;
            }
        }

        public ushort GetLastGoodQuantity()
        {
            lock (_modbusLock)
            {
                return _lastGoodQuantity;
            }
        }

        #endregion

        #region Escritura Modbus

        public bool WriteOnlyRegister(
            byte slaveId,
            ushort registerAddress,
            short writeValue)
        {
            lock (_modbusLock)
            {
                try
                {
                    if (_modbusClient == null)
                    {
                        Debug.WriteLine("[Modbus] Cliente no inicializado.");
                        return false;
                    }

                    _modbusClient.WriteSingleRegister(
                        slaveId,
                        registerAddress,
                        writeValue);

                    Console.WriteLine(
                        "[Modbus] Escritura OK. Dir real: " +
                        registerAddress.ToString() +
                        " Valor: " +
                        writeValue.ToString());

                    // Actualizar cache si la dirección escrita está dentro de la última lectura.
                    UpdateCacheAfterWrite(registerAddress, writeValue);

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error escribiendo registro Modbus: " + ex.Message);
                    return false;
                }
            }
        }

        public bool WriteMultipleRegisters(
            byte slaveId,
            ushort startAddress,
            ushort[] writeValues)
        {
            lock (_modbusLock)
            {
                try
                {
                    if (_modbusClient == null)
                    {
                        Debug.WriteLine("[Modbus] Cliente no inicializado.");
                        return false;
                    }

                    _modbusClient.WriteMultipleRegisters(
                        slaveId,
                        startAddress,
                        writeValues);

                    Console.WriteLine("[Modbus] Escritura múltiple OK.");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error escribiendo múltiples registros Modbus: " + ex.Message);
                    return false;
                }
            }
        }

        private void UpdateCacheAfterWrite(
            ushort registerAddress,
            short value)
        {
            if (_lastGoodData == null || _lastGoodData.Length == 0)
            {
                return;
            }

            if (registerAddress < _lastGoodStartAddress)
            {
                return;
            }

            int index = registerAddress - _lastGoodStartAddress;

            if (index >= 0 && index < _lastGoodData.Length)
            {
                _lastGoodData[index] = value;

                Console.WriteLine(
                    "[Modbus] Cache actualizada. Registro cache[" +
                    index.ToString() +
                    "] = " +
                    value.ToString());
            }
        }

        #endregion
    }
}