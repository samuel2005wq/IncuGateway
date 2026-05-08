using nanoFramework.M2Mqtt;
using nanoFramework.M2Mqtt.Messages;
using Proyecto.Modbus;
using System;
using System.Text;
using System.Threading;

namespace Proyecto
{
    public class MqttService
    {
        private MqttClient _client;

        private readonly ModbusDriver _modbus;
        private readonly string _broker;
        private readonly int _port;

        private readonly byte _slaveId;
        private readonly ushort _startAddress;
        private readonly ushort _quantity;
        private readonly int _reportTimeMs;

        private string _topicPub;
        private string _topicSub;

        public MqttService(
            string broker,
            int port,
            ModbusDriver modbus,
            byte slaveId,
            ushort startAddress,
            ushort quantity,
            int reportTimeMs)
        {
            _broker = broker;
            _port = port;
            _modbus = modbus;
            _slaveId = slaveId;
            _startAddress = startAddress;
            _quantity = quantity;
            _reportTimeMs = reportTimeMs;
        }

        public void Start(string topicPub, string topicSub)
        {
            _topicPub = topicPub;
            _topicSub = topicSub;

            new Thread(MqttLoop).Start();
        }

        private void MqttLoop()
        {
            string clientId = "IncuGateway_" + Guid.NewGuid().ToString();

            while (true)
            {
                try
                {
                    if (_client == null || !_client.IsConnected)
                    {
                        Console.WriteLine("[MQTT] Conectando a broker: " + _broker);

                        _client = new MqttClient(
                            _broker,
                            _port,
                            false,
                            null,
                            null,
                            MqttSslProtocols.None);

                        _client.MqttMsgPublishReceived += OnMessageReceived;

                        _client.Connect(clientId);

                        if (_client.IsConnected)
                        {
                            Console.WriteLine("[MQTT] Conectado correctamente");

                            _client.Subscribe(
                                new string[] { _topicSub },
                                new MqttQoSLevel[] { MqttQoSLevel.AtLeastOnce });

                            Console.WriteLine("[MQTT] Suscrito a: " + _topicSub);
                        }
                        else
                        {
                            Console.WriteLine("[MQTT] No se pudo conectar");
                        }
                    }

                    if (_client != null && _client.IsConnected)
                    {
                        short[] datos = _modbus.ReadRegistersFlexible(
                            _slaveId,
                            _startAddress,
                            _quantity
                        );

                        if (datos != null && datos.Length > 0)
                        {
                            string json = BuildRegistersJson(datos);

                            _client.Publish(
                                _topicPub,
                                Encoding.UTF8.GetBytes(json));

                            Console.WriteLine("[MQTT] Publicado JSON con " + datos.Length.ToString() + " registros");
                        }
                        else
                        {
                            Console.WriteLine("[Modbus] No se recibieron registros");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[MQTT Error] " + ex.Message);
                }

                Thread.Sleep(_reportTimeMs);
            }
        }

        private string BuildRegistersJson(short[] datos)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("{");
            sb.Append("\"device\":\"INCUGATEWAY\",");
            sb.Append("\"slaveId\":" + _slaveId.ToString() + ",");
            sb.Append("\"registers\":[");

            for (int i = 0; i < datos.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }

                ushort visibleAddress = (ushort)(40001 + _startAddress + i);

                sb.Append("{");
                sb.Append("\"address\":" + visibleAddress.ToString() + ",");
                sb.Append("\"raw\":" + datos[i].ToString());
                sb.Append("}");
            }

            sb.Append("]");
            sb.Append("}");

            return sb.ToString();
        }

        private void OnMessageReceived(object sender, MqttMsgPublishEventArgs e)
        {
            try
            {
                string json = Encoding.UTF8.GetString(
                    e.Message,
                    0,
                    e.Message.Length);

                Console.WriteLine("[MQTT] Comando recibido: " + json);

                ModbusCommand cmd = ParseCommand(json);

                ushort realAddress = NormalizeAddress(cmd.Direccion);

                bool ok = _modbus.WriteOnlyRegister(
                    _slaveId,
                    realAddress,
                    cmd.Valor);

                if (ok)
                {
                    Console.WriteLine(
                        "[Modbus] Comando ejecutado. Valor: " +
                        cmd.Valor.ToString() +
                        " Direccion: " +
                        realAddress.ToString());
                }
                else
                {
                    Console.WriteLine("[Modbus] Error ejecutando comando");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MQTT] Error procesando comando: " + ex.Message);
            }
        }

        private ModbusCommand ParseCommand(string json)
        {
            ModbusCommand cmd = new ModbusCommand();

            try
            {
                string[] partes = json.Split(',');

                for (int i = 0; i < partes.Length; i++)
                {
                    string parte = partes[i];

                    if (parte.Contains("Direccion") ||
                        parte.Contains("direccion") ||
                        parte.Contains("address") ||
                        parte.Contains("register"))
                    {
                        string value = GetValueAfterColon(parte);
                        cmd.Direccion = ushort.Parse(value);
                    }

                    if (parte.Contains("Valor") ||
                        parte.Contains("valor") ||
                        parte.Contains("value"))
                    {
                        string value = GetValueAfterColon(parte);
                        cmd.Valor = short.Parse(value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MQTT] Error parseando comando: " + ex.Message);
            }

            return cmd;
        }

        private string GetValueAfterColon(string text)
        {
            int index = text.IndexOf(':');

            if (index < 0)
            {
                return "0";
            }

            string value = text.Substring(index + 1);

            value = value.Trim(new char[]
            {
                ' ', '\r', '\n', '\t', '"', '{', '}'
            });

            return value;
        }

        private ushort NormalizeAddress(ushort address)
        {
            if (address >= 40001)
            {
                return (ushort)(address - 40001);
            }

            return address;
        }
    }
}