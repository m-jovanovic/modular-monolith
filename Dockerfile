FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet restore src/Api/EquipmentLibrary.Api.csproj --locked-mode
RUN dotnet publish src/Api/EquipmentLibrary.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0
WORKDIR /app
COPY --from=build /app/ .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EquipmentLibrary.Api.dll"]
