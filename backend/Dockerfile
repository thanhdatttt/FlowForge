FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/FlowForge.Domain/FlowForge.Domain.csproj src/FlowForge.Domain/
COPY src/FlowForge.Application/FlowForge.Application.csproj src/FlowForge.Application/
COPY src/FlowForge.Infrastructure/FlowForge.Infrastructure.csproj src/FlowForge.Infrastructure/
COPY src/FlowForge.Api/FlowForge.Api.csproj src/FlowForge.Api/
RUN dotnet restore src/FlowForge.Api/FlowForge.Api.csproj

COPY src/ src/
RUN dotnet publish src/FlowForge.Api/FlowForge.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FlowForge.Api.dll"]
