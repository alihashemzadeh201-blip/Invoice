$filePath = "c:\Users\Ali\Sync\Projects\Invoice\Invoice\Pages\InvoiceManage\InvoiceItems.cshtml.cs"

# Read raw bytes
$bytes = [System.IO.File]::ReadAllBytes($filePath)
Write-Host ("File size: " + $bytes.Length + " bytes")
Write-Host ("First 4 bytes (BOM check): " + [System.BitConverter]::ToString($bytes[0..3]))

# Try reading with windows-1256 (Persian/Arabic encoding)
$enc1256 = [System.Text.Encoding]::GetEncoding(1256)
$content1256 = $enc1256.GetString($bytes)
Write-Host "=== First 300 chars with windows-1256 ==="
Write-Host $content1256.Substring(0, [Math]::Min(300, $content1256.Length))

# Try reading with UTF-8 without BOM
$encUtf8 = [System.Text.Encoding]::UTF8
$contentUtf8 = $encUtf8.GetString($bytes)
Write-Host "=== First 300 chars with UTF-8 ==="
Write-Host $contentUtf8.Substring(0, [Math]::Min(300, $contentUtf8.Length))
