# Pokemon API Solution

This solution contains both .NET APIs and a React.js frontend for managing Pokemon data.

## Project Structure

```
API/
├── Inner.API/          # Core Pokemon API (Port 5001)
├── Main.API/           # Summary API (Port 5002)  
├── Frontend/           # React.js TypeScript App (Port 3000)
├── API.sln            # Visual Studio Solution File
└── README.md          # This file
```

## Projects Overview

### 1. Inner.API (Port 5001)
- **Purpose**: Core Pokemon data service
- **Features**: 
  - Pokemon CRUD operations
  - SQL Server database integration
  - Redis caching
  - Swagger documentation
  - Automatic database creation

### 2. Main.API (Port 5002)
- **Purpose**: Pokemon summary service
- **Features**:
  - Aggregates data from Inner.API
  - Provides formatted Pokemon summaries
  - Swagger documentation

### 3. Frontend (Port 3000)
- **Purpose**: React.js web application
- **Features**:
  - Pokemon search interface
  - Integration with both APIs
  - TypeScript support
  - Responsive design

## Prerequisites

- .NET 8.0 SDK
- Node.js 16+ and npm
- SQL Server LocalDB 
- Redis (optional - for caching)

## Getting Started

### 1. Start the Backend APIs

#### Option A: Start All Projects Individually
```bash
# Terminal 1 - Inner.API
cd Inner.API
dotnet run --urls=http://localhost:5001

# Terminal 2 - Main.API  
cd Main.API
dotnet run --urls=http://localhost:5002
```

#### Option B: Use Watch Mode (Recommended for Development)
```bash
# Terminal 1 - Inner.API with hot reload
cd Inner.API
dotnet watch run --urls=http://localhost:5001

# Terminal 2 - Main.API with hot reload
cd Main.API  
dotnet watch run --urls=http://localhost:5002
```

### 2. Start the Frontend

```bash
# Terminal 3 - React App
cd Frontend
npm start
```

The React app will start on http://localhost:3000

## API Endpoints

### Inner.API (http://localhost:5001)
- `GET /` - Swagger UI
- `GET /pokemon/{name}` - Get Pokemon by name
- `GET /swagger/v1/swagger.json` - OpenAPI specification

### Main.API (http://localhost:5002)
- `GET /` - Swagger UI  
- `GET /summary/{name}` - Get Pokemon summary
- `GET /swagger/v1/swagger.json` - OpenAPI specification

### Frontend (http://localhost:3000)
- Main page with Pokemon search interface
- Displays both summary and detailed information

## Database Configuration

The Inner.API automatically creates the SQL Server database and tables on first run. 

**Connection String** (configured in appsettings.json):
```
Server=(localdb)\mssqllocaldb;Database=pokemon_db;Trusted_Connection=true;MultipleActiveResultSets=true
```

## Development Workflow

1. **Backend Changes**: Use `dotnet watch run` for hot reload
2. **Frontend Changes**: React dev server automatically reloads
3. **API Testing**: Use Swagger UI at http://localhost:5001 and http://localhost:5002
4. **Database**: Automatically managed by Entity Framework

## Troubleshooting

### File Locking Issues
If you get file locking errors during build:
```bash
# Stop all dotnet processes
taskkill /f /im dotnet.exe

# Then rebuild
dotnet build
```

### Port Conflicts
If ports are in use, modify the `--urls` parameter:
```bash
dotnet run --urls=http://localhost:5003  # Use different port
```

### Node.js Version
Some packages require Node.js 18+. Current warnings can be ignored for development.

## Technology Stack

- **Backend**: .NET 8, Entity Framework Core, SQL Server, Redis, Swagger
- **Frontend**: React 19, TypeScript, Axios, CSS3
- **Development**: Hot reload, Swagger documentation, Git integration

## Git Workflow

This solution keeps all projects in a single repository for easier management:
- All projects share the same Git history
- Single clone/pull for entire stack
- Coordinated versioning across frontend and backend

## Next Steps

- Add authentication
- Implement Pokemon image storage
- Add more Pokemon data fields
- Deploy to cloud platforms
- Add unit tests