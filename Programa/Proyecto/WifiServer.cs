using System;
using System.Device.Wifi;
using System.Net.NetworkInformation;
using System.Threading;

namespace Proyecto
{
    public class WifiService
    {
        public static string GetIP()
        {
            try
            {
                NetworkInterface[] interfaces =
                    NetworkInterface.GetAllNetworkInterfaces();

                for (int i = 0; i < interfaces.Length; i++)
                {
                    string ip = interfaces[i].IPv4Address;

                    if (ip != null &&
                        ip != string.Empty &&
                        ip != "0.0.0.0")
                    {
                        return ip;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WiFi] Error obteniendo IP: " + ex.Message);
            }

            return "0.0.0.0";
        }

        public static void WaitForIP()
        {
            Console.WriteLine("[WiFi] Esperando IP...");

            while (GetIP() == "0.0.0.0")
            {
                Thread.Sleep(500);
            }

            Console.WriteLine("[WiFi] IP asignada: " + GetIP());
        }

        public void Connect(string ssid, string password)
        {
            new Thread(() =>
            {
                try
                {
                    WifiAdapter[] adapters = WifiAdapter.FindAllAdapters();

                    if (adapters == null || adapters.Length == 0)
                    {
                        Console.WriteLine("[WiFi] No se encontro adaptador WiFi");
                        return;
                    }

                    WifiAdapter wifi = adapters[0];

                    Console.WriteLine("[WiFi] Conectando a: " + ssid);

                    WifiConnectionResult result = wifi.Connect(
                        ssid,
                        WifiReconnectionKind.Automatic,
                        password);

                    if (result.ConnectionStatus == WifiConnectionStatus.Success)
                    {
                        WaitForIP();
                        Console.WriteLine("[WiFi] Conectado correctamente");
                    }
                    else
                    {
                        Console.WriteLine("[WiFi] Error de conexion: " +
                            result.ConnectionStatus.ToString());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[WiFi] Error: " + ex.Message);
                }
            }).Start();
        }
    }
}