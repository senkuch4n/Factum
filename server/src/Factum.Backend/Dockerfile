# Build en la arquitectura de la máquina que compila (rápido en Apple Silicon) y publish cruzado
# a la arquitectura destino (linux/amd64 para la PC del estudio: ver deploy/windows/armar-paquete.sh).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY Factum.Backend.csproj .
RUN dotnet restore -a $TARGETARCH
COPY . .
RUN dotnet publish -c Release -a $TARGETARCH --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# tzdata: la zona IANA de Report:TimeZone (fecha y hora de la inspección en el informe) tiene
# que existir en la imagen; sin ella ReportSettings cae a UTC-03:00 fijo.
# curl: lo usa el healthcheck del compose (GET /health).
# Sin LibreOffice: el informe se entrega en DOCX (HU instalacion-local-docker).
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080
# El puerto lo fija Program.cs (ListenAnyIP(PORT), default 8080): no hace falta --urls.
ENTRYPOINT ["dotnet", "Factum.Backend.dll"]
