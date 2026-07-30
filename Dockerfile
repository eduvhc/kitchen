FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/EmailService/EmailService.csproj src/EmailService/
RUN dotnet restore src/EmailService/EmailService.csproj

COPY src/ src/
RUN dotnet publish src/EmailService/EmailService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app ./
USER $APP_UID
ENTRYPOINT ["dotnet", "EmailService.dll"]
