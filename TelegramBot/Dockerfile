# Қадами 1: Насб кардани .NET 9 (SDK) барои сохтани барнома
FROM mcr.microsoft.com/dotnet/nightly:9.0-sdk AS build

# Директорияи корӣ
WORKDIR /app

# Нусха бардории ҳама файлҳо ба контейнер
COPY . .

# Барпо кардани (build) лоиҳа
RUN dotnet publish -c Release -o out

# Қадами 2: Насб кардани Runtime-и .NET 9
FROM mcr.microsoft.com/dotnet/nightly:9.0-runtime AS runtime

# Гузаштан ба директорияи корӣ
WORKDIR /app

# Нусха бардории файли барпо кардашуда
COPY --from=build /app/out .

# Фармони иҷрошаванда (боти моро оғоз мекунад)
CMD ["dotnet", "TelegramBot.dll"]
