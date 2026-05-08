using Proyecto.Modbus;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Proyecto
{
    public class HttpServer
    {
        private readonly ModbusDriver _modbus;
        private readonly MqttService _mqtt;

        private readonly byte _slaveId;
        private readonly ushort _startAddress;
        private readonly ushort _quantity;

        private HttpListener _listener;

        public HttpServer(
            ModbusDriver modbus,
            MqttService mqtt,
            byte slaveId,
            ushort startAddress,
            ushort quantity)
        {
            _modbus = modbus;
            _mqtt = mqtt;
            _slaveId = slaveId;
            _startAddress = startAddress;
            _quantity = quantity;
        }

        public void Start()
        {
            _listener = new HttpListener("http");
            _listener.Start();

            Console.WriteLine("[HTTP] Servidor web iniciado");

            new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        HttpListenerContext context = _listener.GetContext();

                        new Thread(() =>
                        {
                            try
                            {
                                Handle(context);
                            }
                            catch (Exception exThread)
                            {
                                Console.WriteLine("[HTTP] Error en hilo cliente: " + exThread.Message);
                            }
                        }).Start();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[HTTP] Error aceptando cliente: " + ex.Message);
                        Thread.Sleep(100);
                    }
                }
            }).Start();
        }

        private void Handle(HttpListenerContext ctx)
        {
            string url = string.Empty;
            string method = string.Empty;

            try
            {
                url = ctx.Request.RawUrl;
                method = ctx.Request.HttpMethod;

                if (url == null || url == string.Empty)
                {
                    SafeClose(ctx);
                    return;
                }

                Console.WriteLine("[HTTP] " + method + " " + url);

                if (url == "/favicon.ico")
                {
                    ctx.Response.StatusCode = 204;
                    SafeClose(ctx);
                    return;
                }

                // ========================================================
                // GET / o /index.html
                // Sirve la interfaz desde la microSD por bloques
                // ========================================================
                if (url == "/" || url == "/index.html")
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/html; charset=UTF-8";

                    bool ok = SdConfigHelper.SendFileToStream(
                        SdConfigHelper.IndexPath,
                        ctx.Response.OutputStream);

                    SafeClose(ctx);

                    if (!ok)
                    {
                        Console.WriteLine("[HTTP] No se pudo enviar index.html desde SD");
                    }
                }

                // ========================================================
                // GET /api/data
                // NO lee Modbus directamente.
                // Devuelve la última lectura buena obtenida por MQTT loop.
                // ========================================================
                else if (url == "/api/data" && method == "GET")
                {
                    short[] datos = _modbus.GetLastGoodData();

                    string json = datos != null && datos.Length > 0
                        ? BuildJson(datos)
                        : "{\"error\":\"sin datos\",\"registros\":[]}";

                    Send(ctx, json, "application/json");
                }

                // ========================================================
                // GET /api/config
                // Devuelve config MQTT actual
                // ========================================================
                else if (url == "/api/config" && method == "GET")
                {
                    string cfg = SdConfigHelper.ReadAllText(
                        SdConfigHelper.MqttConfigPath);

                    Send(ctx, ConfigTextToJson(cfg, "mqtt"), "application/json");
                }

                // ========================================================
                // POST /api/config
                // Guarda nueva config MQTT
                // ========================================================
                else if (url == "/api/config" && method == "POST")
                {
                    string body = ReadBody(ctx);

                    string textToSave = NormalizeMqttConfigBody(body);

                    bool ok = SdConfigHelper.WriteAllText(
                        SdConfigHelper.MqttConfigPath,
                        textToSave);

                    Send(ctx,
                        ok ? "{\"status\":\"ok\"}" : "{\"status\":\"error\"}",
                        "application/json");
                }

                // ========================================================
                // GET /api/wifi-config
                // Devuelve config WiFi actual
                // ========================================================
                else if (url == "/api/wifi-config" && method == "GET")
                {
                    string cfg = SdConfigHelper.ReadAllText(
                        SdConfigHelper.WifiConfigPath);

                    Send(ctx, ConfigTextToJson(cfg, "wifi"), "application/json");
                }

                // ========================================================
                // POST /api/wifi-config
                // Guarda config WiFi
                // ========================================================
                else if (url == "/api/wifi-config" && method == "POST")
                {
                    string body = ReadBody(ctx);

                    string textToSave = NormalizeWifiConfigBody(body);

                    bool ok = SdConfigHelper.WriteAllText(
                        SdConfigHelper.WifiConfigPath,
                        textToSave);

                    Send(ctx,
                        ok ? "{\"status\":\"ok\"}" : "{\"status\":\"error\"}",
                        "application/json");
                }

                // ========================================================
                // GET /api/modbus-config
                // Devuelve config Modbus
                // ========================================================
                else if (url == "/api/modbus-config" && method == "GET")
                {
                    string cfg = SdConfigHelper.ReadAllText(
                        SdConfigHelper.ModbusConfigPath);

                    Send(ctx, ConfigTextToJson(cfg, "modbus"), "application/json");
                }

                // ========================================================
                // POST /api/modbus-config
                // Guarda config Modbus
                // ========================================================
                else if (url == "/api/modbus-config" && method == "POST")
                {
                    string body = ReadBody(ctx);

                    string textToSave = NormalizeModbusConfigBody(body);

                    bool ok = SdConfigHelper.WriteAllText(
                        SdConfigHelper.ModbusConfigPath,
                        textToSave);

                    Send(ctx,
                        ok ? "{\"status\":\"ok\"}" : "{\"status\":\"error\"}",
                        "application/json");
                }

                // ========================================================
                // GET /api/register-config
                // Devuelve config de registros
                // ========================================================
                else if (url == "/api/register-config" && method == "GET")
                {
                    string cfg = SdConfigHelper.ReadAllText(
                        SdConfigHelper.RegisterConfigPath);

                    if (cfg == null || cfg == string.Empty)
                    {
                        cfg = "reg0=40001,Registro 0,0,1,u";
                    }

                    Send(ctx, "{\"raw\":\"" + EscapeJson(cfg) + "\"}", "application/json");
                }

                // ========================================================
                // POST /api/register-config
                // Guarda config de registros
                // ========================================================
                else if (url == "/api/register-config" && method == "POST")
                {
                    string body = ReadBody(ctx);

                    bool ok = SdConfigHelper.WriteAllText(
                        SdConfigHelper.RegisterConfigPath,
                        body);

                    Send(ctx,
                        ok ? "{\"status\":\"ok\"}" : "{\"status\":\"error\"}",
                        "application/json");
                }

                // ========================================================
                // POST /api/command
                // Escribe un registro Modbus
                // ========================================================
                else if (url == "/api/command" && method == "POST")
                {
                    string body = ReadBody(ctx);

                    Console.WriteLine("[HTTP] Body comando: " + body);

                    if (body == null || body == string.Empty)
                    {
                        Send(ctx, "{\"status\":\"error\",\"msg\":\"body vacio\"}", "application/json");
                        return;
                    }

                    ModbusCommand cmd = ParseCommand(body);

                    Console.WriteLine(
                        "[HTTP] Cmd parseado. Direccion=" +
                        cmd.Direccion.ToString() +
                        " Valor=" +
                        cmd.Valor.ToString());

                    if (cmd.Direccion == 0)
                    {
                        Send(ctx, "{\"status\":\"error\",\"msg\":\"direccion invalida\"}", "application/json");
                        return;
                    }

                    ushort realAddress = NormalizeAddress(cmd.Direccion);

                    Console.WriteLine(
                        "[HTTP] Escribiendo Modbus. Dir real=" +
                        realAddress.ToString() +
                        " Valor=" +
                        cmd.Valor.ToString());

                    bool ok = _modbus.WriteOnlyRegister(
                        _slaveId,
                        realAddress,
                        cmd.Valor);

                    Send(ctx,
                        ok ? "{\"status\":\"ok\"}" : "{\"status\":\"error\"}",
                        "application/json");
                }

                // ========================================================
                // Ruta no encontrada
                // ========================================================
                else
                {
                    ctx.Response.StatusCode = 404;
                    Send(ctx, "Not found", "text/plain");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HTTP] Handle error: " + ex.Message);

                try
                {
                    if (ctx != null)
                    {
                        ctx.Response.StatusCode = 500;
                        Send(ctx, "Error HTTP interno", "text/plain");
                    }
                }
                catch
                {
                    SafeClose(ctx);
                }
            }
        }

        private string BuildJson(short[] data)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("{");

            if (data.Length > 0)
            {
                sb.Append("\"temp\":" + data[0].ToString() + ",");
            }
            else
            {
                sb.Append("\"temp\":0,");
            }

            if (data.Length > 2)
            {
                sb.Append("\"hum\":" + data[2].ToString() + ",");
            }
            else if (data.Length > 1)
            {
                sb.Append("\"hum\":" + data[1].ToString() + ",");
            }
            else
            {
                sb.Append("\"hum\":0,");
            }

            sb.Append("\"quantity\":" + data.Length.ToString() + ",");
            sb.Append("\"registros\":[");

            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }

                ushort visibleAddress = (ushort)(40001 + _startAddress + i);

                sb.Append("{");
                sb.Append("\"addr\":" + visibleAddress.ToString() + ",");
                sb.Append("\"val\":" + data[i].ToString());
                sb.Append("}");
            }

            sb.Append("]");
            sb.Append("}");

            return sb.ToString();
        }

        private string ReadBody(HttpListenerContext ctx)
        {
            try
            {
                long length = ctx.Request.ContentLength64;

                if (length <= 0)
                {
                    Console.WriteLine("[HTTP] Body vacío o sin ContentLength");
                    return string.Empty;
                }

                if (length > 256)
                {
                    Console.WriteLine("[HTTP] Body demasiado grande: " + length.ToString());
                    return string.Empty;
                }

                byte[] buffer = new byte[(int)length];

                int totalRead = 0;

                while (totalRead < length)
                {
                    int read = ctx.Request.InputStream.Read(
                        buffer,
                        totalRead,
                        (int)length - totalRead);

                    if (read <= 0)
                    {
                        break;
                    }

                    totalRead += read;
                }

                return Encoding.UTF8.GetString(buffer, 0, totalRead);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HTTP] Error leyendo body: " + ex.Message);
                return string.Empty;
            }
        }

        private void Send(
            HttpListenerContext ctx,
            string body,
            string mime)
        {
            try
            {
                byte[] buf = Encoding.UTF8.GetBytes(body);

                ctx.Response.ContentType = mime;
                ctx.Response.ContentLength64 = buf.Length;

                ctx.Response.OutputStream.Write(
                    buf,
                    0,
                    buf.Length);

                SafeClose(ctx);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HTTP] Send error: " + ex.Message);
                SafeClose(ctx);
            }
        }

        private void SafeClose(HttpListenerContext ctx)
        {
            try
            {
                if (ctx != null)
                {
                    ctx.Response.Close();
                }
            }
            catch
            {
            }
        }

        // ============================================================
        // Parser para comandos HTTP
        // Acepta:
        // Direccion:40006,Valor:50
        // {"Direccion":40006,"Valor":50}
        // {"address":40006,"value":50}
        // ============================================================
        private ModbusCommand ParseCommand(string text)
        {
            ModbusCommand cmd = new ModbusCommand();

            try
            {
                string[] parts = text.Split(',');

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];

                    if (ContainsDireccion(part))
                    {
                        string value = GetValueAfterSeparator(part);
                        cmd.Direccion = ushort.Parse(value);
                    }

                    if (ContainsValor(part))
                    {
                        string value = GetValueAfterSeparator(part);
                        cmd.Valor = short.Parse(value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HTTP] Error parseando comando: " + ex.Message);
            }

            return cmd;
        }

        private bool ContainsDireccion(string text)
        {
            return text.Contains("Direccion") ||
                   text.Contains("direccion") ||
                   text.Contains("Dirección") ||
                   text.Contains("dirección") ||
                   text.Contains("address") ||
                   text.Contains("register");
        }

        private bool ContainsValor(string text)
        {
            return text.Contains("Valor") ||
                   text.Contains("valor") ||
                   text.Contains("value");
        }

        private string GetValueAfterSeparator(string text)
        {
            int index = text.IndexOf(':');

            if (index < 0)
            {
                index = text.IndexOf('=');
            }

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

        // ============================================================
        // Helpers para devolver config en JSON simple
        // ============================================================
        private string ConfigTextToJson(string cfg, string type)
        {
            if (cfg == null)
            {
                cfg = string.Empty;
            }

            if (type == "wifi")
            {
                return "{"
                    + "\"ssid\":\"" + EscapeJson(GetConfigValue(cfg, "ssid")) + "\","
                    + "\"password\":\"" + EscapeJson(GetConfigValue(cfg, "password")) + "\","
                    + "\"dhcp\":true"
                    + "}";
            }

            if (type == "mqtt")
            {
                return "{"
                    + "\"broker\":\"" + EscapeJson(GetConfigValue(cfg, "broker")) + "\","
                    + "\"port\":" + DefaultIfEmpty(GetConfigValue(cfg, "port"), "1883") + ","
                    + "\"user\":\"" + EscapeJson(GetConfigValue(cfg, "user")) + "\","
                    + "\"password\":\"" + EscapeJson(GetConfigValue(cfg, "password")) + "\","
                    + "\"topicPub\":\"" + EscapeJson(GetConfigValue(cfg, "topicPub")) + "\","
                    + "\"topicSub\":\"" + EscapeJson(GetConfigValue(cfg, "topicSub")) + "\","
                    + "\"reportTimeMs\":" + DefaultIfEmpty(GetConfigValue(cfg, "reportTimeMs"), "5000")
                    + "}";
            }

            if (type == "modbus")
            {
                return "{"
                    + "\"slaveId\":" + DefaultIfEmpty(GetConfigValue(cfg, "slaveId"), "1") + ","
                    + "\"baudRate\":" + DefaultIfEmpty(GetConfigValue(cfg, "baudRate"), "9600") + ","
                    + "\"dataBits\":" + DefaultIfEmpty(GetConfigValue(cfg, "dataBits"), "8") + ","
                    + "\"parity\":\"" + EscapeJson(GetConfigValue(cfg, "parity")) + "\","
                    + "\"stopBits\":\"" + EscapeJson(GetConfigValue(cfg, "stopBits")) + "\","
                    + "\"txPin\":" + DefaultIfEmpty(GetConfigValue(cfg, "txPin"), "17") + ","
                    + "\"rxPin\":" + DefaultIfEmpty(GetConfigValue(cfg, "rxPin"), "16") + ","
                    + "\"timeoutMs\":" + DefaultIfEmpty(GetConfigValue(cfg, "timeoutMs"), "1000") + ","
                    + "\"startAddress\":" + DefaultIfEmpty(GetConfigValue(cfg, "startAddress"), "0") + ","
                    + "\"quantity\":" + DefaultIfEmpty(GetConfigValue(cfg, "quantity"), "16")
                    + "}";
            }

            return "{\"raw\":\"" + EscapeJson(cfg) + "\"}";
        }

        private string GetConfigValue(string cfg, string key)
        {
            try
            {
                string[] lines = cfg.Split('\n');

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
            catch
            {
            }

            return string.Empty;
        }

        private string DefaultIfEmpty(string value, string defaultValue)
        {
            if (value == null || value == string.Empty)
            {
                return defaultValue;
            }

            return value;
        }

        private string NormalizeWifiConfigBody(string body)
        {
            // Si viene como key=value, se guarda tal cual.
            if (body.IndexOf('=') >= 0 && body.IndexOf('{') < 0)
            {
                return body;
            }

            string ssid = ExtractJsonValue(body, "ssid");
            string password = ExtractJsonValue(body, "password");

            return "ssid=" + ssid + "\r\n" +
                   "password=" + password + "\r\n";
        }

        private string NormalizeMqttConfigBody(string body)
        {
            if (body.IndexOf('=') >= 0 && body.IndexOf('{') < 0)
            {
                return body;
            }

            string broker = ExtractJsonValue(body, "broker");
            string port = ExtractJsonValue(body, "port");
            string user = ExtractJsonValue(body, "user");
            string password = ExtractJsonValue(body, "password");
            string topicPub = ExtractJsonValue(body, "topicPub");
            string topicSub = ExtractJsonValue(body, "topicSub");
            string reportTimeMs = ExtractJsonValue(body, "reportTimeMs");

            return "broker=" + broker + "\r\n" +
                   "port=" + DefaultIfEmpty(port, "1883") + "\r\n" +
                   "user=" + user + "\r\n" +
                   "password=" + password + "\r\n" +
                   "topicPub=" + topicPub + "\r\n" +
                   "topicSub=" + topicSub + "\r\n" +
                   "reportTimeMs=" + DefaultIfEmpty(reportTimeMs, "5000") + "\r\n";
        }

        private string NormalizeModbusConfigBody(string body)
        {
            if (body.IndexOf('=') >= 0 && body.IndexOf('{') < 0)
            {
                return body;
            }

            string slaveId = ExtractJsonValue(body, "slaveId");
            string baudRate = ExtractJsonValue(body, "baudRate");
            string dataBits = ExtractJsonValue(body, "dataBits");
            string parity = ExtractJsonValue(body, "parity");
            string stopBits = ExtractJsonValue(body, "stopBits");
            string txPin = ExtractJsonValue(body, "txPin");
            string rxPin = ExtractJsonValue(body, "rxPin");
            string timeoutMs = ExtractJsonValue(body, "timeoutMs");
            string startAddress = ExtractJsonValue(body, "startAddress");
            string quantity = ExtractJsonValue(body, "quantity");

            return "slaveId=" + DefaultIfEmpty(slaveId, "1") + "\r\n" +
                   "com=COM2\r\n" +
                   "baudRate=" + DefaultIfEmpty(baudRate, "9600") + "\r\n" +
                   "dataBits=" + DefaultIfEmpty(dataBits, "8") + "\r\n" +
                   "parity=" + DefaultIfEmpty(parity, "None") + "\r\n" +
                   "stopBits=" + DefaultIfEmpty(stopBits, "One") + "\r\n" +
                   "txPin=" + DefaultIfEmpty(txPin, "17") + "\r\n" +
                   "rxPin=" + DefaultIfEmpty(rxPin, "16") + "\r\n" +
                   "timeoutMs=" + DefaultIfEmpty(timeoutMs, "1000") + "\r\n" +
                   "startAddress=" + DefaultIfEmpty(startAddress, "0") + "\r\n" +
                   "quantity=" + DefaultIfEmpty(quantity, "16") + "\r\n";
        }

        private string ExtractJsonValue(string json, string key)
        {
            try
            {
                string pattern = "\"" + key + "\"";
                int keyIndex = json.IndexOf(pattern);

                if (keyIndex < 0)
                {
                    return string.Empty;
                }

                int colonIndex = json.IndexOf(':', keyIndex);

                if (colonIndex < 0)
                {
                    return string.Empty;
                }

                int start = colonIndex + 1;

                while (start < json.Length &&
                       (json[start] == ' ' ||
                        json[start] == '\r' ||
                        json[start] == '\n' ||
                        json[start] == '\t' ||
                        json[start] == '"'))
                {
                    start++;
                }

                int end = start;

                while (end < json.Length &&
                       json[end] != ',' &&
                       json[end] != '}' &&
                       json[end] != '"')
                {
                    end++;
                }

                return json.Substring(start, end - start).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private string EscapeJson(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }
                
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '"')
                {
                    sb.Append("\\\"");
                }
                else if (c == '\\')
                {
                    sb.Append("\\\\");
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}