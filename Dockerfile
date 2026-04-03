FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file first to maximize restore layer cache reuse.
COPY Smena/Host.csproj Smena/Host.csproj
RUN dotnet restore Smena/Host.csproj

COPY ./Smena ./Smena
WORKDIR /src/Smena

# Build Tailwind CSS
ADD https://github.com/tailwindlabs/tailwindcss/releases/download/v3.4.17/tailwindcss-linux-x64 /usr/local/bin/tailwindcss
RUN chmod +x /usr/local/bin/tailwindcss \
    && tailwindcss -i wwwroot/css/input.css -o wwwroot/css/root-ui.css --minify

RUN dotnet publish Host.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5001;http://+:5000
EXPOSE 5001
EXPOSE 5000

ENTRYPOINT ["dotnet", "Host.dll"]
