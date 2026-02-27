FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file first to maximize restore layer cache reuse.
COPY Smena/Host.csproj Smena/Host.csproj
RUN dotnet restore Smena/Host.csproj

COPY ./Smena ./Smena
WORKDIR /src/Smena
RUN dotnet publish Host.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5001;http://+:5000
EXPOSE 5001
EXPOSE 5000

ENTRYPOINT ["dotnet", "Host.dll"]
