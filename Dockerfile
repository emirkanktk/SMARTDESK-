FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SmartDesk.slnx ./
COPY src/SmartDesk.Domain/SmartDesk.Domain.csproj src/SmartDesk.Domain/
COPY src/SmartDesk.Application/SmartDesk.Application.csproj src/SmartDesk.Application/
COPY src/SmartDesk.Infrastructure/SmartDesk.Infrastructure.csproj src/SmartDesk.Infrastructure/
COPY src/SmartDesk.API/SmartDesk.API.csproj src/SmartDesk.API/
RUN dotnet restore src/SmartDesk.API/SmartDesk.API.csproj
COPY . .
RUN dotnet publish src/SmartDesk.API/SmartDesk.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SmartDesk.API.dll"]
