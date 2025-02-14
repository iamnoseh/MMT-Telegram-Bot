# Қадами 1: Истифодаи тасвирҳои .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Қадами 2: Феҳристи кориро муайян мекунем
WORKDIR /src

# Қадами 3: Нусхабардории файли .csproj ва барқарорсозии вобастагиҳо
COPY ["TelegramBot/TelegramBot.csproj", "TelegramBot/"]
RUN dotnet restore "TelegramBot/TelegramBot.csproj"

# Қадами 4: Тамоми лоиҳаро нусхабардорӣ мекунем ва сохта мешавем
COPY . .
WORKDIR "/src/TelegramBot"
RUN dotnet build -c Release --no-restore
RUN dotnet publish -c Release -o /app/publish --no-build

# Қадами 5: Тасвири сабук барои иҷрои контейнер
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Қадами 6: Танзими нуқтаи вуруд
ENTRYPOINT ["dotnet", "TelegramBot.dll"]
