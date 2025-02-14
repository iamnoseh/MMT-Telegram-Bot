# Қадами 1: Омода кардани муҳити иҷрои (runtime) .NET 9
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 10000  
# Render.com порти 10000-ро истифода мебарад

# Қадами 2: Омода кардани муҳити таҳия (build environment)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
WORKDIR "/src/TelegramBot"
RUN dotnet restore
RUN dotnet build -c Release -o /app/build

# Қадами 3: Чоп ва паҳнкунӣ (publish)
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

# Қадами 4: Омода кардани имиджи барои иҷро (runtime image)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:10000
ENTRYPOINT ["dotnet", "TelegramBot.dll"]

CMD ["sh", "-c", "echo 'BOT_TOKEN='${7586875361:AAGhLybn-7rwKpwYv-9Bp23yrdJ2sVZbYBw}; dotnet TelegramBot.dll"]

