using System;
using System.Text;

namespace Proyecto
{
    public class ModbusCommand
    {
        public ushort Direccion { get; set; }
        public short Valor { get; set; }
    }
    public class Sensor
    {
        public string Temperatura { get; set; }
        public string Humedad { get; set; }
    }
}