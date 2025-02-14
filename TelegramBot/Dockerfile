# Қадами 1: Омода кардани муҳити иҷрои (runtime) .NET 9
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

# Қадами 2: Омода кардани муҳити таҳия (build environment)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Нусхабардории файлҳои лоиҳа (ба мисол TelegramBot.csproj)
COPY ["TelegramBot/TelegramBot.csproj", "TelegramBot/"]
RUN dotnet restore "TelegramBot/TelegramBot.csproj"

# Нусхабардории ҳамаи файлҳо ва сохтани (build) лоиҳа
COPY . .
WORKDIR "/src/TelegramBot"
RUN dotnet build "TelegramBot.csproj" -c Release -o /app/build

# Қадами 3: Чоп ва паҳнкунӣ (publish)
FROM build AS publish
RUN dotnet publish "TelegramBot.csproj" -c Release -o /app/publish

# Қадами 4: Омода кардани имиджи барои иҷро (runtime image)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramBot.dll"]
