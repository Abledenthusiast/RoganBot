


#FROM mcr.microsoft.com/windows/servercore:ltsc2022 as base
# Install Chocolatey
#RUN powershell -NoProfile -InputFormat None -ExecutionPolicy Bypass -Command "[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'));" && SET "PATH=%PATH%;%ALLUSERSPROFILE%\chocolatey\bin"
#RUN choco install -y dotnet-6.0-sdk


FROM mcr.microsoft.com/dotnet/runtime:6.0.9-windowsservercore-ltsc2022 AS base
WORKDIR /app
COPY *.dll /Windows/System32/


FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["RoganBot.csproj", "./"]
RUN dotnet restore "RoganBot.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "RoganBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RoganBot.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RoganBot.dll"]

