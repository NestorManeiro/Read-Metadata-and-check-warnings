# Plataforma de Detección de Deepfakes para Empresas

Sistema backend diseñado para identificar contenido multimedia manipulado por inteligencia artificial (deepfakes) en tiempo real, ayudando a las empresas a protegerse frente a fraudes, suplantaciones y daños reputacionales.

## Relevancia actual

El 59% de los usuarios afirma tener dificultades para distinguir entre contenido real y manipulado, mientras que los ataques con deepfakes son cada vez más frecuentes y sofisticados. Este tipo de amenazas afecta especialmente a empresas, facilitando fraudes financieros, suplantaciones de identidad y la manipulación de comunicaciones corporativas.

## Tecnologías clave

- **Análisis multimodal:** Combina detección de incoherencias faciales (microexpresiones, parpadeo), desfases audio-visuales y anomalías en metadatos.
- **Modelos de IA avanzados:** Utiliza redes neuronales profundas entrenadas con datasets públicos y privados para identificar patrones sutiles de manipulación.
- **Procesamiento local/cloud:** Opciones on-premise para máxima privacidad o SaaS para escalabilidad inmediata.

## Valor empresarial

- **Prevención de fraudes:** Detecta suplantaciones en videollamadas, transferencias fraudulentas o documentos multimedia falsificados.
- **Protección reputacional:** Identifica contenido manipulado antes de su difusión pública.
- **Cumplimiento normativo:** Facilita auditorías y ayuda a cumplir regulaciones de seguridad digital (GDPR, ISO 27001).

## Diferenciales competitivos

- **Actualización continua:** Modelos reentrenados automáticamente para contrarrestar nuevas técnicas de deepfake.
- **Integración transparente:** API RESTful compatible con sistemas corporativos existentes (correo, CRM, herramientas de videoconferencia).

## Desafíos técnicos

- Requiere hardware especializado (GPUs) para análisis de vídeo en tiempo real.
- La tasa de falsos positivos/negativos se reduce combinando la detección automática con protocolos de verificación humana.

---

Este sistema representa una capa crítica de defensa en un panorama donde los deepfakes se utilizan para ataques empresariales cada 5 minutos, ofreciendo detección proactiva y automatizada para organizaciones de cualquier sector.

## 📝 Lista de comprobaciones (ordenadas por complejidad de implementación)

### Implementación prioritaria (baja complejidad)
- [ ] **Análisis de metadatos**
  - [X] Revisión de datos EXIF (fecha, dispositivo, software de edición)
  - [X] Detección de inconsistencias en metadatos técnicos (resolución, formato, codecs)
  - [X] BlackList de determinadas camaras
  - [ ] Comparación de hashes contra bases de datos de archivos originales

- [ ] **Redes neuronales preentrenadas**
  - [ ] Clasificación real/fake con modelos open-source (Ej: DeepFaceLab detector)
  - [ ] Detección de artefactos de generación (texturas faciales, bordes)

### Implementación intermedia
- [ ] **Análisis de puntos de referencia faciales**
  - [ ] Detección de desalineaciones en landmarks faciales (OpenCV/Dlib)
  - [ ] Incoherencias en parpadeo y movimiento ocular (frecuencia, naturalidad)

- [ ] **Comprobaciones de coherencia temporal (vídeo)**
  - [ ] Análisis de transiciones entre frames (saltos bruscos, interpolación artificial)
  - [ ] Detección de patrones de movimiento no humanos (suavizado excesivo)

### Implementación avanzada (alta complejidad)
- [ ] **Análisis de iluminación y sombras**
  - [ ] Detección de inconsistencias en direcciones de luz
  - [ ] Análisis espectral de reflejos oculares

- [ ] **Detección de flujo sanguíneo en píxeles**
  - [ ] Análisis de microvariaciones de color en zonas faciales
  - [ ] Modelado de patrones de pulso cardíaco en vídeo

### Roadmap futuro
- [ ] **Sincronización audio-visual**
  - [ ] Detección de desfases labio-audio (Phoneme-Viseme Mismatch)
  - [ ] Análisis de artefactos en generación de voz sintética

---

**Priorización técnica:**  
El orden refleja el esfuerzo estimado y los recursos necesarios (desde análisis básico de archivos hasta técnicas que requieren GPU y datasets especializados).  

# Guía rápida de uso de la API DeepFakeDetector (.NET 8)

## Estructura de carpetas del proyecto

- **src/Controllers/**  
  Controladores de la API. Aquí está el punto de entrada de las peticiones HTTP, por ejemplo, `FileController.cs`.

- **src/Models/**  
  Modelos de datos usados para las peticiones y respuestas de la API. Incluye modelos como `ExifResponse`, `ExifSimpleResponse`, `TechWarnings`, etc.

- **src/Services/**  
  Lógica de negocio y análisis. Aquí están servicios como `ExifService` (procesamiento de metadatos) y `TechnicalValidator` (validación técnica).

- **src/Utils/**  
  Funciones auxiliares y validadores de archivos.

- **appsettings.json**  
  Archivo de configuración editable por el usuario, donde se pueden definir resoluciones esperadas para dispositivos y otros parámetros técnicos.

---

## Cómo usar la API

1. **Arranca la API**  
   Ejecuta el proyecto con Visual Studio, Rider o `dotnet run`.  
   La API estará disponible en:
   - `https://localhost:7292` (HTTPS)
   - `http://localhost:5283` (HTTP)

2. **Envía una imagen o vídeo para analizar**  
   Usa Postman o una herramienta similar:
   - Método: `POST`
   - URL: `https://localhost:7292/api/file/analyze`
   - Body: `form-data`
     - Campo `file`: Selecciona el archivo a subir (imagen o vídeo)
     - Campo `DetailedAnalysis`: `true` o `false` (si se omite o es `false`, la respuesta será simplificada)

3. **Modificar parámetros técnicos**  
   El usuario puede editar únicamente el archivo `appsettings.json` para:
   - Añadir o modificar resoluciones esperadas por modelo de dispositivo, por ejemplo:
     ```
     "TechnicalValidation": {
       "ExpectedResolutions": {
         "iPhone 13": "1170x2532|1284x2778",
         "Canon EOS R5": "4480x6720|4128x6192"
       }
     }
     ```
   - Los cambios en este archivo afectan la validación técnica de los archivos subidos.

---

## Respuesta esperada en Postman

- **Si `DetailedAnalysis` es `false` o no se envía:**
