# Многоступенчатая сборка сервиса Users. Контекст сборки — корень репозитория:
#   docker build -f Services/Users/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только проекты: слой restore кешируется, пока не меняются зависимости.
COPY Services/Users/ProjectWork.Users.Domain/ProjectWork.Users.Domain.csproj Services/Users/ProjectWork.Users.Domain/
COPY Services/Users/ProjectWork.Users.Application/ProjectWork.Users.Application.csproj Services/Users/ProjectWork.Users.Application/
COPY Services/Users/ProjectWork.Users.Infrastructure/ProjectWork.Users.Infrastructure.csproj Services/Users/ProjectWork.Users.Infrastructure/
COPY Services/Users/ProjectWork.Users.Presentation/ProjectWork.Users.Presentation.csproj Services/Users/ProjectWork.Users.Presentation/
RUN dotnet restore Services/Users/ProjectWork.Users.Presentation/ProjectWork.Users.Presentation.csproj

COPY Services/Users/ Services/Users/
RUN dotnet publish Services/Users/ProjectWork.Users.Presentation/ProjectWork.Users.Presentation.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Npgsql при подключении ищет GSSAPI; без библиотеки в логах появляется ошибка загрузки.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
# Непривилегированный пользователь из базового образа.
USER $APP_UID
ENTRYPOINT ["dotnet", "ProjectWork.Users.Presentation.dll"]
