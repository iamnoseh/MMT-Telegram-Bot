# Қадами 1: Истифода аз тасвири асосии ASP.NET 9.0
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

# Қадами 2: Сохтани барнома бо тасвири SDK
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["TelegramBotApp.csproj", "./"]
RUN dotnet restore "./TelegramBot.csproj"
COPY . .
RUN dotnet publish "TelegramBot.csproj" -c Release -o /app/publish

# Қадами 3: Омода кардани тасвири ниҳоӣ
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TelegramBot.dll"]
