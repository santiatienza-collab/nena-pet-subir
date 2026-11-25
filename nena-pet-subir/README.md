# 🐱 Nena Pet

**Una mascota de escritorio gruñona e ilustrada, basada en una gata real.** Vive en la esquina inferior de tu pantalla, con fondo transparente, y reacciona a lo que haces: golpea el teclado cuando escribes, te sigue con la mirada, se enfada si tecleas muy rápido y ronronea cuando la dejas tranquila.

Versión nativa y ligera para **Windows** (C# + WPF, sin Electron, ~2,5 MB) y **macOS** (Swift + AppKit).

---

## ✨ Qué hace

### ⌨️ Cuando escribes
- Golpea las teclas con las patas, **alternando una pata distinta en cada pulsación**, y se iluminan las teclas bajo cada pata.
- **Los primeros golpes de cada racha son torpes**, como si un humano la sujetara por detrás y aporreara el teclado con sus patas estiradas. Poco a poco toma el control y golpea con precisión.
- Cuanto más rápido escribes, más se enfada: aparece una **vena roja estilo anime** en la sien y el ceño se frunce.
- **A más de 10 teclas por segundo** salen explosiones con teclas volando.
- Si te pasas con el ritmo, termina cansada y despeinada.
- Funciona con **cualquier teclado e idioma**, incluidos los editores de entrada chinos, japoneses y coreanos (IME).

### 👀 Cuando mueves el ratón
- Sus **ojos, dibujados en vivo, siguen el cursor** de forma continua y suave, con pequeños movimientos propios y parpadeos naturales (poco frecuentes).
- Mueve las orejas de vez en cuando, con una sacudida ligera.

### 😴 Cuando la dejas tranquila
Tras unos segundos sin tocar nada, alterna tres modos:

| Modo | Cara |
|---|---|
| **Relajada** | Ojos entornados, **run run** real con temblor suave y un colmillo asomando |
| **Grumpy** | Su cara gruñona de siempre, mirando al cursor (aparece 3 veces menos que la relajada) |
| **Extremadamente atenta** | Ojos muy abiertos, pupilas enormes, orejas en alerta, sigue el cursor muy rápido |

También se aburre: bosteza, mira hacia arriba, apoya la barbilla o se duerme.

### 🖱️ Cuando haces clic sobre ella
Abre la boca y **maúlla**, con la animación sincronizada con el audio.

---

## 🔊 Sonidos
Maullido y ronroneo **reales** (grabaciones, no sintetizados), con latencia mínima. Puedes poner los tuyos:
- **Windows:** `%APPDATA%\NenaPet\sonidos`
- **macOS:** `~/Library/Application Support/NenaPet/sonidos`

Ficheros `.wav` cuyo nombre empiece por `miau`/`meow` (maullidos) o `purr`/`run` (ronroneos). Los tuyos sustituyen a los de serie.

---

## 🎛️ Controles
- **Arrastrar** a Nena para moverla donde quieras.
- **Clic** para que maúlle.
- **Clic derecho** (o el icono de la bandeja / barra de menús): tamaño (Mini / Normal / Grande), ocultar, sonidos, iniciar con el sistema y salir.

---

## 📦 Instalación

### Windows
Ejecuta `NenaPetSetup.exe`. Instala por usuario (sin permisos de administrador), crea accesos directos y permite arrancar con Windows. También hay una versión portable (`NenaPet.exe`).

> El ejecutable no está firmado, así que Windows o tu antivirus pueden avisar la primera vez.

### macOS
1. Descarga `NenaPet.dmg` y arrastra **Nena Pet** a Aplicaciones.
2. Primera vez: clic derecho → **Abrir** (la app no está firmada con cuenta de Apple).
3. Concede el permiso de **Accesibilidad** (Ajustes del Sistema → Privacidad y seguridad) para que vea cuándo escribes.

---

## 🛠️ Compilar

**Windows** (solo necesita el compilador `csc.exe` que ya trae Windows):
```powershell
cd NenaWindows
powershell -File build.ps1              # NenaPet.exe
powershell -File build-installer.ps1    # NenaPetSetup.exe
```

**macOS** (necesita las herramientas de línea de comandos de Xcode):
```bash
cd NenaMac && ./build-mac.sh            # dist/NenaPet.dmg
```

El repositorio incluye una acción de GitHub (`.github/workflows/build-mac.yml`) que genera el `.dmg` en un servidor macOS, sin necesidad de tener un Mac.

---

## 🧩 Cómo está hecho
- **Animación:** fotogramas ilustrados alineados con la misma cámara (sin saltos), con fundidos cortos entre poses.
- **Ojos, vena, orejas y patas:** piezas independientes dibujadas y animadas en vivo sobre el fotograma base, por eso la mirada y los golpes son continuos y no a saltos.
- **Entrada:** lectura del estado del teclado y del ratón del sistema, sin ganchos globales y **sin guardar qué tecla pulsas**. Nena solo cuenta pulsaciones para animarse.
- **Privacidad:** no usa red ni guarda nada de lo que escribes.

---

## 📝 Créditos
Gata original: **Nena** 🐾. Los créditos de los sonidos están en `NenaMac/Resources/sounds/CREDITS.txt`.
