# Многоступенчатая сборка сервиса Bookings. Контекст сборки — корень репозитория:
#   docker build -f Services/Bookings/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только проекты: слой restore кешируется, пока не меняются зависимости.
COPY Shared/ProjectWork.Contracts/ProjectWork.Contracts.csproj Shared/ProjectWork.Contracts/
COPY Services/Bookings/ProjectWork.Bookings.Domain/ProjectWork.Bookings.Domain.csproj Services/Bookings/ProjectWork.Bookings.Domain/
COPY Services/Bookings/ProjectWork.Bookings.Application/ProjectWork.Bookings.Application.csproj Services/Bookings/ProjectWork.Bookings.Application/
COPY Services/Bookings/ProjectWork.Bookings.Infrastructure/ProjectWork.Bookings.Infrastructure.csproj Services/Bookings/ProjectWork.Bookings.Infrastructure/
COPY Services/Bookings/ProjectWork.Bookings.Presentation/ProjectWork.Bookings.Presentation.csproj Services/Bookings/ProjectWork.Bookings.Presentation/
RUN dotnet restore Services/Bookings/ProjectWork.Bookings.Presentation/ProjectWork.Bookings.Presentation.csproj

COPY Shared/ProjectWork.Contracts/ Shared/ProjectWork.Contracts/
COPY Services/Bookings/ Services/Bookings/
RUN dotnet publish Services/Bookings/ProjectWork.Bookings.Presentation/ProjectWork.Bookings.Presentation.csproj \
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
ENTRYPOINT ["dotnet", "ProjectWork.Bookings.Presentation.dll"]
