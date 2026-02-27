# syntax=docker/dockerfile:1.7-labs

FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

# Copy project file first to maximize restore layer cache reuse.
COPY Smena/Host.csproj Smena/Host.csproj
RUN --mount=type=cache,id=smena-nuget,target=/root/.nuget/packages \
    dotnet restore Smena/Host.csproj

COPY . .
RUN --mount=type=cache,id=smena-nuget,target=/root/.nuget/packages \
    dotnet publish Smena/Host.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5001;http://+:5000
EXPOSE 5001
EXPOSE 5000

ENTRYPOINT ["dotnet", "Host.dll"]
