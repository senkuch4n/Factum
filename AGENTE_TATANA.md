
# Tatana agente
## Listado de casos de uso
- Instalar Librerias (pymobiledevice3, adb, etc.)
- Actualizar Librerias
- Mostrar Estado de conexion del agente con el servidor
- Configurar Link del servidor
- Configurar que se inicie agente tatana al prender la maquina del usuario factum
- Visualizar dispositivos conectados disponibles
- Permitir Minimizar la aplicacion y dejarla abierta en segundo plano

### Caso de uso: Instalar Librerias
> #### Actor principal: Usuario Factum
> #### Actor secundario: -
 #### Pre-condiciones:
  - Tatana agente instalado en la computadora.
  - computadora del usuario con acceso a internet
  - Tatana agente con permisos para poder descargar e instalar cosas en la computadora

#### Post-condiciones:
  - Libreria/s descargadas en la computadora del usuario 

| Actor principal  | Sistema |
| ------------- |:-------------:|
| 1 - Inicia instalacion de librerias      |      |
|      | 2 - Muestra listado de librerias disponibles sin descargar     |
| 3 - selecciona una o varias librerias para descargar||
||4 - Muestra barra de progreso de descarga
||5 - Muestra barra de progreso completa|
||6 - Muestra icono de que la libreria se descargo|
||7 - Fin de caso de uso.|

### Caso de uso: Actualizar  librerias
> #### Actor principal: Usuario Factum
> #### Actor secundario: -
#### Pre-condiciones:
 - Al menos una libreria disponible para actualizar
 - Tatana agente instalado en la computadora del usuario
 - Computadora del usuario con acceso a internet

#### Post-condiciones:
 - Libreria/s actualizada/s

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia Actualizacion||
||2 - Muestra listado de librerias disponibles para actualizar|
|3 - Selecciona libreria/s para actualizar||
||4 - Muestra barra de progreso de actualizacion|
||5 - Muestra barra de progreso completa|
||6 - Muestra icono de que la libreria se actualizo|
||7 - Fin de caso de uso|



### Caso de uso: Mostrar estado de conexion del agente con el servidor

> #### Actor principal: Usuario factum
> #### Actor secundario: -
#### Pre-condiciones:
 - Computadora del usuario con acceso a internet
 - Link del servidor configurado

#### Post-condiciones:
 - Usuario informado de la conexion del agente tatana con el servidor

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia caso de uso||
||2 - Muestra estado de conexion del agente tatana con el servidor|
||3 - Habilita boton para reiniciar/reintentar conexion|
||4 - Fin de caso de uso|


### Caso de uso: Configurar link del servidor
> #### Actor principal: Usuario factum
> #### Actor secundario: -
#### Pre-condiciones:
 - Agente tatana instalado en la computadora del usuario
 - Computadora del usuario con conexion a internet
#### Post-condiciones:
 - Agente tatana conectado al servidor

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia configuracion del link del servidor||
||2 - Solicita ingresar link del servidor|
|3 - Escribe link del servidor||
||4 - Valida link del servidor|
||5 - Muestra mensaje de que el link es correcto y esta conectado al servidor|
|6 - Acepta mensaje||
||7 - Guarda configuracion|
||8 - Fin de caso de uso|

### Caso de uso: Configurar que se inicie agente tatana al prender la maquina del usuario factum

> #### Actor principal: Usuario factum
> #### Actor secundario: -

#### Pre-condiciones:
 - Agente tatana instalado en la computadora del usuario

#### Post-condiciones:
 - Agente tatana se iniciara cada vez que se prenda la computadora del usuario en segundo plano

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia caso de uso||
||2 - Pregunta si se quiere iniciar el agente cada vez que se prende la conputadora del usuario|
|3 - Selecciona que si quiere iniciar el agente cada vez que se prende la computadora del usuario||
||4 - Guarda configuracion|
||5 - Fin de caso de uso|

### Caso de uso: Visualizar dispositivos conectados disponibles
> #### Actor principal: Usuario factum
> #### Actor secundario: -

#### Pre-condiciones:
 - Agente tatana instalado en la computadora del usuario
 - Al menos un dispositivo conectado a la computadora
#### Post-condiciones:
 - Listado de dispositivos detectados por el agente

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia caso de uso||
||2 - Muestra listado de dispositivos conectados a la PC|
||3 - Fin de caso de uso|


### Caso de uso: Permitir Minimizar la aplicacion y dejarla abierta en segundo plano
> #### Actor principal: Usuario factum
 > #### Actor secundario: -

 #### Pre-condiciones:
  - Agente tatana instalado en la computadora
#### Post-condiciones:
 - Agente tatana minimizado en la computadora del usuario

|Actor principal|Sistema|
|--|:--:|
|1 - Inicia caso de uso||
||2 - Muestra ventana del agente tatana|
|3 - Minimiza el agente||
||4 - Cierra ventana|
||5 - Queda abierto en segundo plano|
||6 - Fin de caso de uso|
