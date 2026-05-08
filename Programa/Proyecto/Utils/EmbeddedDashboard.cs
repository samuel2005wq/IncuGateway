namespace Proyecto
{
    internal static class EmbeddedDashboard
    {
        public const string Html =
@"<!DOCTYPE html>
<html lang='es'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>INCUGATEWAY</title>
<style>
*{box-sizing:border-box}
body{
    margin:0;
    font-family:Arial, sans-serif;
    background:#0a0e14;
    color:#e8f4fd;
}
header{
    background:#111827;
    border-bottom:1px solid #1e3a5f;
    padding:16px 20px;
}
h1{
    margin:0;
    color:#00c8ff;
    font-size:24px;
}
.subtitle{
    color:#8baab8;
    font-size:13px;
    margin-top:4px;
}
main{
    padding:16px;
}
.grid{
    display:grid;
    grid-template-columns:repeat(auto-fit,minmax(220px,1fr));
    gap:12px;
}
.card{
    background:#111827;
    border:1px solid #1e3a5f;
    border-radius:12px;
    padding:14px;
}
.card h2{
    margin:0 0 10px 0;
    font-size:16px;
    color:#8baab8;
}
.kpi{
    font-size:34px;
    font-weight:bold;
}
.temp{color:#ff8c00}
.hum{color:#00c8ff}
.ok{color:#00e676}
.warn{color:#ffd600}
button{
    background:#00c8ff;
    color:#001827;
    border:0;
    border-radius:8px;
    padding:10px 14px;
    font-weight:bold;
    cursor:pointer;
}
input{
    width:100%;
    padding:9px;
    margin-top:4px;
    margin-bottom:8px;
    border-radius:6px;
    border:1px solid #1e3a5f;
    background:#141d2b;
    color:white;
}
table{
    width:100%;
    border-collapse:collapse;
    margin-top:10px;
}
th,td{
    border-bottom:1px solid #1e3a5f;
    padding:7px;
    font-size:13px;
    text-align:left;
}
th{
    color:#8baab8;
}
pre{
    background:#050b12;
    border-radius:8px;
    padding:10px;
    overflow:auto;
    color:#8baab8;
}
.footer{
    color:#4a6a7a;
    font-size:12px;
    margin-top:14px;
}
</style>
</head>
<body>
<header>
    <h1>INCUGATEWAY</h1>
    <div class='subtitle'>Gateway Modbus RTU → MQTT / HTTP · Interfaz embebida</div>
</header>

<main>
    <div class='grid'>
        <div class='card'>
            <h2>Temperatura</h2>
            <div class='kpi temp' id='tempVal'>--</div>
            <div class='footer'>Registro 40001</div>
        </div>

        <div class='card'>
            <h2>Humedad</h2>
            <div class='kpi hum' id='humVal'>--</div>
            <div class='footer'>Registro 40003</div>
        </div>

        <div class='card'>
            <h2>Estado Modbus</h2>
            <div class='kpi ok' id='stateVal'>--</div>
            <div class='footer' id='lastUpdate'>Esperando datos...</div>
        </div>
    </div>

    <div class='card' style='margin-top:12px'>
        <h2>Registros Modbus</h2>
        <button onclick='loadData()'>Actualizar ahora</button>
        <table>
            <thead>
                <tr>
                    <th>#</th>
                    <th>Dirección</th>
                    <th>Valor raw</th>
                </tr>
            </thead>
            <tbody id='regBody'>
                <tr><td colspan='3'>Sin datos</td></tr>
            </tbody>
        </table>
    </div>

    <div class='card' style='margin-top:12px'>
        <h2>Comando Modbus</h2>
        <label>Dirección visible</label>
        <input id='addr' type='number' value='40006'>
        <label>Valor</label>
        <input id='val' type='number' value='380'>
        <button onclick='sendCmd()'>Enviar comando</button>
        <pre id='cmdResult'>Sin comando enviado</pre>
    </div>

    <div class='card' style='margin-top:12px'>
        <h2>JSON recibido</h2>
        <pre id='rawJson'>Esperando lectura...</pre>
    </div>
</main>

<script>
function scaleTemp(raw){
    return (raw * 0.1).toFixed(1);
}

function loadData(){
    fetch('/api/data')
    .then(function(r){ return r.json(); })
    .then(function(data){
        document.getElementById('rawJson').textContent =
            JSON.stringify(data,null,2);

        if(data.error){
            document.getElementById('stateVal').textContent='ERROR';
            document.getElementById('stateVal').className='kpi warn';
            return;
        }

        document.getElementById('stateVal').textContent='OK';
        document.getElementById('stateVal').className='kpi ok';

        if(data.registros && data.registros.length>0){
            document.getElementById('tempVal').textContent =
                scaleTemp(data.registros[0].val) + ' C';
        }

        if(data.registros && data.registros.length>2){
            document.getElementById('humVal').textContent =
                scaleTemp(data.registros[2].val) + ' %';
        }

        var body='';
        if(data.registros){
            for(var i=0;i<data.registros.length;i++){
                body += '<tr>';
                body += '<td>'+(i+1)+'</td>';
                body += '<td>'+data.registros[i].addr+'</td>';
                body += '<td>'+data.registros[i].val+'</td>';
                body += '</tr>';
            }
        }

        document.getElementById('regBody').innerHTML = body;
        document.getElementById('lastUpdate').textContent =
            'Última actualización: ' + new Date().toLocaleTimeString();
    })
    .catch(function(e){
        document.getElementById('stateVal').textContent='ERROR';
        document.getElementById('stateVal').className='kpi warn';
        document.getElementById('rawJson').textContent='Error: '+e;
    });
}

function sendCmd(){
    var a = document.getElementById('addr').value;
    var v = document.getElementById('val').value;

    var body = 'Direccion:' + a + ',Valor:' + v;

    fetch('/api/command',{
        method:'POST',
        body:body
    })
    .then(function(r){ return r.text(); })
    .then(function(t){
        document.getElementById('cmdResult').textContent=t;
    })
    .catch(function(e){
        document.getElementById('cmdResult').textContent='Error: '+e;
    });
}

setInterval(loadData,3000);
loadData();
</script>
</body>
</html>";
    }
}