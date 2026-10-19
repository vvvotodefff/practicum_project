# Многоступенчатая сборка сервиса Events. Контекст сборки — корень репозитория:
#   docker build -f Services/Events/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только проекты: слой restore кешируется, пока не меняются зависимости.
COPY Shared/ProjectWork.Contracts/ProjectWork.Contracts.csproj Shared/ProjectWork.Contracts/
COPY Services/Events/ProjectWork.Events.Domain/ProjectWork.Events.Domain.csproj Services/Events/ProjectWork.Events.Domain/
COPY Services/Events/ProjectWork.Events.Application/ProjectWork.Events.Application.csproj Services/Events/ProjectWork.Events.Application/
COPY Services/Events/ProjectWork.Events.Infrastructure/ProjectWork.Events.Infrastructure.csproj Services/Events/ProjectWork.Events.Infrastructure/
COPY Services/Events/ProjectWork.Events.Presentation/ProjectWork.Events.Presentation.csproj Services/Events/ProjectWork.Events.Presentation/
RUN dotnet restore Services/Events/ProjectWork.Events.Presentation/ProjectWork.Events.Presentation.csproj

COPY Shared/ProjectWork.Contracts/ Shared/ProjectWork.Contracts/
COPY Services/Events/ Services/Events/
RUN dotnet publish Services/Events/ProjectWork.Events.Presentation/ProjectWork.Events.Presentation.csproj \
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
ENTRYPOINT ["dotnet", "ProjectWork.Events.Presentation.dll"]
