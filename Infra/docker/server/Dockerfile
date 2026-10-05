# 서버 이미지. 빌드 컨텍스트는 저장소 루트에서 지정한다.
#   docker build -t crimson-tide-server -f Infra/docker/server/Dockerfile .
#   docker run --rm -p 8080:8080 crimson-tide-server
#
# 주의: 프로젝트 디렉터리는 대소문자를 구분하는 Linux에서도 동작하도록
#       실제 경로와 동일한 "Server"를 사용한다.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 프로젝트 파일만 먼저 복사해 restore 결과를 캐시한다.
COPY Server/CrimsonTide.Server.csproj Server/
COPY Infra/CrimsonTide.Infra.csproj Infra/
RUN dotnet restore Server/CrimsonTide.Server.csproj

# 나머지 소스를 복사해 Release 게시물을 만든다.
COPY Server/ Server/
COPY Infra/ Infra/
RUN dotnet publish Server/CrimsonTide.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# 컨테이너 안에서는 모든 인터페이스로 바인딩한다. (Program.cs는 URL을 하드코딩하지 않는다)
ENV ASPNETCORE_URLS=http://+:8080
# 저장 데이터는 볼륨에 둔다. CRIMSON_DB_PATH가 설정 파일보다 우선한다.
ENV CRIMSON_DB_PATH=/data/crimson-tide.db
VOLUME ["/data"]
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CrimsonTide.Server.dll"]
