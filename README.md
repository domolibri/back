# DomoLibri Backend

Este é o backend do projeto DomoLibri, desenvolvido em .NET 8 utilizando uma arquitetura modular com PostgreSQL.

## 🛠️ Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker e Docker Compose](https://www.docker.com/products/docker-desktop)
- [EF Core Tools](https://learn.microsoft.com/pt-br/ef/core/cli/dotnet) (para migrações)

---

## 🏗️ Como fazer o Build

Para compilar o projeto localmente, execute os seguintes comandos na raiz da pasta `backend/src`:

```bash
cd src
dotnet build
```

---

## 🐳 Como executar com Docker

A aplicação está configurada para rodar facilmente com Docker Compose, que subirá tanto a API quanto o banco de dados PostgreSQL.

Na raiz da pasta `backend`, execute:

```bash
# Para subir os containers
docker compose up -d

# Para visualizar os logs
docker compose logs -f
```

A API estará acessível em `http://localhost:8080/swagger` (se o Swagger estiver habilitado no ambiente de Development).

---

## 🗄️ Gerenciamento de Banco de Dados (Migrations)

As migrações são gerenciadas pelo Entity Framework Core. Os comandos devem ser executados dentro da pasta `backend/src`.

### 1. Instalar Ferramentas do EF (se necessário)

Caso ainda não tenha o `dotnet-ef` instalado globalmente:

```bash
dotnet tool install --global dotnet-ef
```

### 2. Criar uma nova Migração

Sempre que houver alterações nas entidades do domínio, crie uma nova migração:

```bash
dotnet ef migrations add NomeDaSuaMigracao \
    --project DomoLibri.Infrastructure \
    --startup-project DomoLibri.Api \
    --output-dir Data/Migrations
```

### 3. Atualizar o Banco de Dados

Para aplicar as migrações pendentes ao banco de dados:

#### Localmente (PostgreSQL rodando via Docker ou Local)
Certifique-se de que o banco de dados está acessível conforme a string de conexão no `appsettings.json`.

```bash
dotnet ef database update \
    --project DomoLibri.Infrastructure \
    --startup-project DomoLibri.Api
```

#### Via Docker ou Automático
No arquivo `Program.cs`, você pode adicionar o seguinte bloco após o `app.MapControllers();` e antes de `app.Run();` para que as migrações sejam aplicadas automaticamente ao iniciar o container:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DomoLibriDbContext>();
    db.Database.Migrate();
}
```

Isso garante que o banco esteja sempre atualizado sem comandos manuais.

---

## 📝 Variáveis de Ambiente

As configurações principais podem ser sobrescritas via variáveis de ambiente no arquivo `docker-compose.yml`:

- `ConnectionStrings__DefaultConnection`: String de conexão com o banco.
- `Jwt__Secret`: Chave secreta para geração de tokens JWT.
- `ASPNETCORE_ENVIRONMENT`: Define o ambiente (Development/Production).
