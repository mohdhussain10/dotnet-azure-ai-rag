# =================================================
# STAGE 1: Base Runtime (The production runner environment)
# ======================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# ========================================================
# STAGE 2: Build Environment (Contains the full .NET 10 SDK)
# ========================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

#we are copyinh only csproj file before dotnet restore because docker will compare
#if there is any change in the cache it will invalidate cachec from that point and run dotnet restore again
#take 30-60 sec so everytime it restore which is not required.
#to prevent we copied only csproj file because it will be same and it will not run dotnet restore and it will only run
#dotnet restore if there is a change in csproj file meanse adding any reference

# Step 2A: Copy project files ONLY (Docker Layer Caching optimization)
COPY ["RAG.API/RAG.API.csproj", "RAG.API/"]
COPY ["RAG.Domain/RAG.Domain.csproj", "RAG.Domain/"]
COPY ["RAG.Infrastructure/RAG.Infrastructure.csproj", "RAG.Infrastructure/"]

# Step 2B: Restore NuGet dependencies
RUN dotnet restore "RAG.API/RAG.API.csproj"


#we only build startup project as it is best practice and .Net behind the scene will build all other projects
#as there is dependency graph. we don't build solution because solution can have test project and we don't push test project in docker image
# Step 2C: Copy the remaining source code and compile
COPY . .
WORKDIR "/src/RAG.API"
RUN dotnet build "RAG.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ========================================================
# STAGE 3: Publish (Strips debugging symbols and bundles DLLs)
# ==========================================
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "RAG.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
#/p:UseAppHost=false: Tells .NET not to generate an OS-specific executable host wrapper (like RAG.API.exe),
#but instead publish the portable cross-platform RAG.API.dll. This keeps the output purely portable across Linux
#environments.

# ========================================================
# STAGE 4: Final Image (Ultra-lean, secure production image)
# ========================================================
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RAG.API.dll"]

