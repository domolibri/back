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

Todas as configurações são centralizadas no arquivo **`.env`** na raiz da pasta `backend/`.

### Configuração inicial

```bash
cp .env.example .env
# Edite .env e defina as suas senhas e segredos
```

> **Atenção:** O arquivo `.env` contém segredos e está no `.gitignore`. Nunca o commite no repositório.

### Variáveis disponíveis

| Variável | Descrição | Padrão |
|---|---|---|
| `DB_USER` | Usuário do PostgreSQL | `postgres` |
| `DB_PASSWORD` | Senha do PostgreSQL | — |
| `DB_NAME` | Nome do banco de dados | `domolibri_db` |
| `JWT_SECRET` | Chave secreta JWT (mín. 32 chars) | — |
| `FRONTEND_URL` | URL do frontend (CORS e e-mails) | `http://localhost:4200` |
| `SMTP_SENDER_NAME` | Nome do remetente dos e-mails | `Domo Libri` |
| `SMTP_SENDER_EMAIL` | E-mail do remetente | `no-reply@domolibri.com.br` |
| `MP_MAX_MESSAGES` | Máx. de mensagens no Mailpit | `500` |
| `BLOB_PUBLIC_ENDPOINT` | URL pública do Azurite (navegador) | `http://localhost:10000/devstoreaccount1` |
| `ASPNETCORE_ENVIRONMENT` | Ambiente ASP.NET Core | `Development` |

### Serviços de infraestrutura (Docker)

| Serviço | URL | Descrição |
|---|---|---|
| API | `http://localhost:8080/swagger` | Backend .NET 8 |
| Mailpit | `http://localhost:8025` | Interface de e-mails (dev) |
| Azurite | `http://localhost:10000` | Emulador Azure Blob Storage |
| PostgreSQL | `localhost:5432` | Banco de dados |

### Persistência do Mailpit

Os e-mails são salvos em `./data/mailpit/mailpit.db` no host, permitindo que as mensagens sobrevivam ao restart dos containers.

### Blob Storage (Azurite)

Para o desenvolvimento local (fora do Docker), o backend conecta-se ao Azurite via `localhost:10000`. Dentro do Docker Compose, usa o hostname `azurite:10000`. A variável `BLOB_PUBLIC_ENDPOINT` garante que as URLs retornadas pela API apontem para o endereço acessível pelo navegador.
