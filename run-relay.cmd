@echo off
rem Chay server relay tren may nay (ws://<IP may nay>:8080). Ban be o mang khac can mo cong 8080 tren router.
dotnet run --project "%~dp0src\LolPing.Relay" -c Release
pause
