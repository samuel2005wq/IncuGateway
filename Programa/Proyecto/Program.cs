using System;
using System.IO.Ports;
using System.Threading;
using Proyecto.Modbus;
using nanoFramework.Networking;

namespace Proyecto
{
    public class Program
    {
        private static ModbusDriver _modbus;

        public static void Main()
        {
            Console.WriteLine("==================================");
            Console.WriteLine(" INCUGATEWAY - Modbus RTU a MQTT");
            Console.WriteLine("==================================");

            // ============================================================
            // 1. Montar microSD y verificar archivos base
            // ============================================================
            bool sdOk = SdConfigHelper.Mount();

            if (sdOk)
            {
                SdConfigHelper.EnsureFiles();
                Console.WriteLine("[Main] microSD lista");
            }
            else
            {
                Console.WriteLine("[Main] No se pudo montar la microSD.");
                Console.WriteLine("[Main] Se usaran valores por defecto.");
            }

            // ============================================================
            // 2. Cargar configuracion WiFi desde SD
            // ============================================================
            string ssid = GetConfigOrDefault(
                SdConfigHelper.WifiConfigPath,
                "ssid",
                "NombreDeTuRed");

            string pass = GetConfigOrDefault(
                SdConfigHelper.WifiConfigPath,
                "password",
                "ContrasenaDeTuRed");

            // ============================================================
            // 3. Cargar configuracion MQTT desde SD
            // ============================================================
            string broker = GetConfigOrDefault(
                SdConfigHelper.MqttConfigPath,
                "broker",
                "broker.emqx.io");

            int mqttPort = GetIntConfigOrDefault(
                SdConfigHelper.MqttConfigPath,
                "port",
                1883);

            string topicPub = GetConfigOrDefault(
                SdConfigHelper.MqttConfigPath,
                "topicPub",
                "dent2026/grupoX/sensores");

            string topicSub = GetConfigOrDefault(
                SdConfigHelper.MqttConfigPath,
                "topicSub",
                "dent2026/grupoX/comandos");

            int reportTimeMs = GetIntConfigOrDefault(
                SdConfigHelper.MqttConfigPath,
                "reportTimeMs",
                5000);

            if (reportTimeMs <= 0)
            {
                reportTimeMs = 5000;
            }

            // ============================================================
            // 4. Cargar configuracion Modbus desde SD
            // ============================================================
            int slaveIdInt = GetIntConfigOrDefault(
                SdConfigHelper.ModbusConfigPath,
                "slaveId",
                1);

            int baudRate = GetIntConfigOrDefault(
                SdConfigHelper.ModbusConfigPath,
                "baudRate",
                9600);

            int dataBits = GetIntConfigOrDefault(
                SdConfigHelper.ModbusConfigPath,
                "dataBits",
                8);

            int txPin = GetIntConfigOrDefault(
                SdConfigHelper.ModbusConfigPath,
                "txPin",
                17);

            int rxPin = GetIntConfigOrDefault(
                SdConfigHelper.ModbusConfigPath,
                "rxPin",
                16);

            int startAddressInt = GetIntConfigOrDefault(
                SdConfigHelper.ModbusConfigPath,
                "startAddress",
                0);

            int quantityInt = GetIntConfigOrDefault(
                SdConfigHelper.ModbusConfigPath,
                "quantity",
                16);

            if (slaveIdInt < 1 || slaveIdInt > 247)
            {
                slaveIdInt = 1;
            }

            if (startAddressInt < 0)
            {
                startAddressInt = 0;
            }

            if (quantityInt <= 0 || quantityInt > 125)
            {
                quantityInt = 16;
            }

            Parity parity = GetParityConfigOrDefault(
                SdConfigHelper.ModbusConfigPath,
                "parity",
                Parity.None);

            StopBits stopBits = GetStopBitsConfigOrDefault(
                SdConfigHelper.ModbusConfigPath,
                "stopBits",
                StopBits.One);

            byte slaveId = (byte)slaveIdInt;
            ushort startAddress = (ushort)startAddressInt;
            ushort quantity = (ushort)quantityInt;

            Console.WriteLine("[Main] Configuracion cargada:");
            Console.WriteLine("[Main] WiFi SSID: " + ssid);
            Console.WriteLine("[Main] MQTT Broker: " + broker);
            Console.WriteLine("[Main] MQTT Port: " + mqttPort.ToString());
            Console.WriteLine("[Main] Topic PUB: " + topicPub);
            Console.WriteLine("[Main] Topic SUB: " + topicSub);
            Console.WriteLine("[Main] Reporte MQTT: " + reportTimeMs.ToString() + " ms");
            Console.WriteLine("[Main] Modbus Slave ID: " + slaveId.ToString());
            Console.WriteLine("[Main] Modbus baud: " + baudRate.ToString());
            Console.WriteLine("[Main] Modbus TX/RX: " + txPin.ToString() + "/" + rxPin.ToString());
            Console.WriteLine("[Main] Modbus startAddress: " + startAddress.ToString());
            Console.WriteLine("[Main] Modbus quantity: " + quantity.ToString());

            // ============================================================
            // 5. Iniciar Modbus
            // ============================================================
            try
            {
                _modbus = new ModbusDriver(
                    baudRate,
                    dataBits,
                    parity,
                    stopBits,
                    txPin,
                    rxPin);

                _modbus.Initialize();

                Console.WriteLine("[Main] Modbus inicializado");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Main] Error inicializando Modbus: " + ex.Message);
            }

            // ============================================================
            // 6. Conectar WiFi
            // ============================================================
            Console.WriteLine("[Main] Conectando a WiFi: " + ssid);

            bool success = false;

            try
            {
                success = WifiNetworkHelper.ScanAndConnectDhcp(
                    ssid,
                    pass,
                    requiresDateTime: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Main] Error WiFi: " + ex.Message);
            }

            if (success)
            {
                WifiService.WaitForIP();

                Console.WriteLine("[Main] WiFi conectado");
                Console.WriteLine("[Main] IP: " + WifiService.GetIP());

                // ========================================================
                // 7. Iniciar MQTT
                // ========================================================
                MqttService mqtt = null;

                try
                {
                    mqtt = new MqttService(
                        broker,
                        mqttPort,
                        _modbus,
                        slaveId,
                        startAddress,
                        quantity,
                        reportTimeMs);

                    mqtt.Start(topicPub, topicSub);

                    Console.WriteLine("[Main] MQTT iniciado");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Main] Error iniciando MQTT: " + ex.Message);
                }

                // ========================================================
                // 8. Iniciar servidor HTTP
                // ========================================================
                try
                {
                    var http = new HttpServer(
                        _modbus,
                        mqtt,
                        slaveId,
                        startAddress,
                        quantity);

                    http.Start();

                    Console.WriteLine("[Main] Servidor listo en http://" + WifiService.GetIP());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Main] Error iniciando HTTP: " + ex.Message);
                }
            }
            else
            {
                Console.WriteLine("[Main] Error de conexion WiFi.");
                Console.WriteLine("[Main] Revisa WifiCfg.txt en la microSD.");
            }

            Thread.Sleep(Timeout.Infinite);
        }

        private static string GetConfigOrDefault(
            string filePath,
            string key,
            string defaultValue)
        {
            string value = string.Empty;

            try
            {
                value = SdConfigHelper.GetValue(filePath, key);
            }
            catch
            {
                value = string.Empty;
            }

            if (value == null || value == string.Empty)
            {
                return defaultValue;
            }

            return value;
        }

        private static int GetIntConfigOrDefault(
            string filePath,
            string key,
            int defaultValue)
        {
            string value = GetConfigOrDefault(filePath, key, string.Empty);

            if (value == string.Empty)
            {
                return defaultValue;
            }

            try
            {
                return int.Parse(value);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static Parity GetParityConfigOrDefault(
            string filePath,
            string key,
            Parity defaultValue)
        {
            string value = GetConfigOrDefault(filePath, key, string.Empty);

            if (value == "None" || value == "Ninguna")
            {
                return Parity.None;
            }

            if (value == "Even" || value == "Par")
            {
                return Parity.Even;
            }

            if (value == "Odd" || value == "Impar")
            {
                return Parity.Odd;
            }

            return defaultValue;
        }

        private static StopBits GetStopBitsConfigOrDefault(
            string filePath,
            string key,
            StopBits defaultValue)
        {
            string value = GetConfigOrDefault(filePath, key, string.Empty);

            if (value == "One" || value == "1")
            {
                return StopBits.One;
            }

            if (value == "Two" || value == "2")
            {
                return StopBits.Two;
            }

            return defaultValue;
        }
    }
}