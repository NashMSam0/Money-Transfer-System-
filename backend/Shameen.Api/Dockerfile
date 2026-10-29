FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/Shameen.Api/Shameen.Api.csproj backend/Shameen.Api/
RUN dotnet restore backend/Shameen.Api/Shameen.Api.csproj
COPY backend/Shameen.Api/ backend/Shameen.Api/
RUN dotnet publish backend/Shameen.Api/Shameen.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Shameen.Api.dll"]
