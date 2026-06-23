FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish AsistenciaColegio.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
EXPOSE 5217
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:5217
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "AsistenciaColegio.dll"]
