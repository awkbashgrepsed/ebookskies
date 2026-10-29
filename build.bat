@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo C# compiler not found. Install .NET Framework 4.5 or later developer components.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:EbookSkiesReader.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.Linq.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll EbookSkiesReader.cs
if errorlevel 1 exit /b %errorlevel%
echo.
echo Built EbookSkiesReader.exe
