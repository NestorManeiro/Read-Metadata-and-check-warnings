# Read Metadata and Check Warnings API

A .NET API for extracting and analyzing image metadata, including warnings, capture date/time, and location information. The API supports both basic and detailed analysis modes.

---

## Endpoints

### 1. Analyze File Metadata

**POST** `/api/File/analyze`

Analyze an uploaded image file and extract its metadata.

#### Request

- **Body Type:** `form-data`
- **Keys:**
  - `file` (type: File): The image file to analyze.
  - `DetailedAnalysis` (type: Boolean): 
    - `true`: Returns all available metadata.
    - `false`: Returns only warnings, capture date/time, and location.

#### Example with Postman

- Set the request type to `POST`.
- URL: `https://127.0.0.1:7292/api/File/analyze`
- In the `Body` tab, select `form-data`.
  - Add a key named `file`, set its type to `File`, and upload the image.
  - Add a key named `DetailedAnalysis`, set its type to `Text`, and value to `true` or `false`.

#### Response

- If `DetailedAnalysis` is `true`, the response contains all metadata found in the image.
- If `DetailedAnalysis` is `false`, the response includes only:
  - Warnings
  - Capture date and time
  - Location (if available)

> **Note:** Some comments in the API responses may appear in Spanish for clarity.

---

### 2. API Health Check

**GET** `/api/File/test`

Check if the API is active.

#### Example

- Set the request type to `GET`.
- URL: `https://127.0.0.1:7292/api/File/test`

#### Response

- Returns a message indicating the API is running.
- _Comment in response is in Spanish._

---

## Quick Start

1. Clone the repository.
2. Build and run the project with .NET.
3. Use Postman or any HTTP client to interact with the endpoints as described above.

---

## Notes

- The API is designed for local use (`https://127.0.0.1:7292` by default).
- The service supports image files (e.g., JPEG, PNG).
- For best results, use images with embedded EXIF metadata.

---
