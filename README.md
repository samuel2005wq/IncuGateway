# IncuGateway

IncuGateway es una aplicación desarrollada en **.NET nanoFramework para ESP32**, orientada a la lectura de datos por **Modbus RTU** y su publicación mediante **MQTT**.  
El sistema utiliza una microSD para almacenar la interfaz web y los archivos de configuración que permiten modificar parámetros del dispositivo sin recompilar el código.

## Descripción general

El proyecto implementa un gateway embebido capaz de:

- Leer registros Modbus RTU desde un dispositivo o simulador esclavo.
- Publicar los datos leídos en formato JSON hacia un broker MQTT.
- Recibir comandos por MQTT o desde la interfaz web.
- Escribir valores en registros Modbus.
- Servir una página web local desde la ESP32.
- Cargar y guardar configuraciones desde una memoria microSD.

## Estructura del repositorio

El repositorio está organizado principalmente en dos carpetas:

```text
IncuGateway/
├── Memoria/
│   ├── index.html
│   ├── ModbusCfg.txt
│   ├── MqttCfg.txt
│   ├── RegisterCfg.txt
│   └── WifiCfg.txt
│
└── Programa/
    ├── Program.cs
    ├── HttpServer.cs
    ├── MqttService.cs
    ├── WifiService.cs
    ├── SdConfigHelper.cs
    ├── MicroSdRawFat32Reader.cs
    ├── ModbusCommand.cs
    └── Modbus/
        └── ModbusDriver.cs
