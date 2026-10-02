FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Factum.Backend.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# tzdata: la zona IANA de Report:TimeZone (fecha y hora de la inspección en el informe) tiene
# que existir en la imagen; sin ella ReportSettings cae a UTC-03:00 fijo.
# ReportService convierte el DOCX del informe a PDF invocando "soffice --headless --convert-to
# pdf" (Services/Reports/ReportService.cs) — sin esto instalado, la generación de informes falla
# siempre en Docker. Solo el componente Writer (no la suite completa) porque es lo único que usa
# esa conversión.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libreoffice-writer tzdata \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Factum.Backend.dll", "--urls", "http://0.0.0.0:8080"]
