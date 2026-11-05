# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Sao chép các tệp csproj để cache restore
COPY ["src/MT_GRMS.Domain/MT_GRMS.Domain.csproj", "src/MT_GRMS.Domain/"]
COPY ["src/MT_GRMS.Application/MT_GRMS.Application.csproj", "src/MT_GRMS.Application/"]
COPY ["src/MT_GRMS.Infrastructure/MT_GRMS.Infrastructure.csproj", "src/MT_GRMS.Infrastructure/"]
COPY ["src/MT_GRMS.Api/MT_GRMS.Api.csproj", "src/MT_GRMS.Api/"]

# Restore dependencies
RUN dotnet restore "src/MT_GRMS.Api/MT_GRMS.Api.csproj"

# Sao chép toàn bộ mã nguồn
COPY . .

# Build và publish Release
WORKDIR "/src/src/MT_GRMS.Api"
RUN dotnet publish "MT_GRMS.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 5232

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MT_GRMS.Api.dll"]

