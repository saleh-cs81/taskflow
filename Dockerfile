# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY TaskFlow.sln ./
COPY src/TaskFlow.Domain/*.csproj src/TaskFlow.Domain/
COPY src/TaskFlow.Application/*.csproj src/TaskFlow.Application/
COPY src/TaskFlow.Infrastructure/*.csproj src/TaskFlow.Infrastructure/
COPY src/TaskFlow.API/*.csproj src/TaskFlow.API/
RUN dotnet restore TaskFlow.sln

COPY . .
RUN dotnet publish src/TaskFlow.API/TaskFlow.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TaskFlow.API.dll"]
