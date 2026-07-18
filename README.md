# Codebase Static Explorer & Database Catalog

A unified codebase static context analysis platform and database ERD visualizer. This project enables developers to scan database schemas (routines, triggers, procedures), C# .NET solutions (Roslyn AST compiler analysis), and Angular Frontend codebases (TypeScript and HTML template elements) into a SQLite database, providing a beautiful visual Web UI dashboard to explore, document, prune, and link components.

---

## Project Structure

The codebase is organized as follows:

```text
├── src/
│   ├── HoangTLM.CodeBase.DatabaseScanner/       # C# Library for database schema scanning
│   ├── HoangTLM.CodeBase.DatabaseScanner.App/   # Console application runner for Database Scanner
│   ├── HoangTLM.CodeBase.DatabaseScanner.Api/   # ASP.NET Core Web API (HTTP backend on port 5080)
│   ├── HoangTLM.CodeBase.DatabaseScanner.Web/   # Angular 16 Frontend Web Client (Foblex Flow canvas)
│   ├── HoangTLM.CodeBase.DotNetScanner/         # C# Library for C# solution scanning (Roslyn AST compiler API)
│   ├── HoangTLM.CodeBase.DotNetScanner.App/     # Console application runner for C# solution scanner
│   └── HoangTLM.CodeBase.WebScanner/            # Node.js 18 TypeScript scanner for Angular TS & HTML templates
├── metadata.db                                  # SQLite database file containing all scanned catalog indices
└── README.md                                    # This project readme file
```

---

## Prerequisites

- **.NET SDK 8.0+** (for C# libraries, console apps, and Web API)
- **Node.js 18+** & **npm** (for WebScanner and Angular Frontend)
- **SQLite3**

---

## How to Run & Scans

### 1. Database Scanner
Scans SQL Server tables, columns, procedures, triggers, and functions.
- Configure target connection strings in `src/HoangTLM.CodeBase.DatabaseScanner.App/App.config`.
- Run:
  ```bash
  dotnet run --project src/HoangTLM.CodeBase.DatabaseScanner.App/HoangTLM.CodeBase.DatabaseScanner.App.csproj
  ```

### 2. .NET Solution Scanner
Parses C# syntax trees recursively using Roslyn to discover classes, methods, enums, constants, Minimal APIs, Queues, and Schedules.
- Configure the target `.sln` file and SQLite connection in `src/HoangTLM.CodeBase.DotNetScanner.App/App.config`.
- Run:
  ```bash
  dotnet run --project src/HoangTLM.CodeBase.DotNetScanner.App/HoangTLM.CodeBase.DotNetScanner.App.csproj
  ```

### 3. Angular Frontend Web Scanner
Parses Angular component classes (using `ts-morph` AST) and HTML templates (using `parse5`), establishing trace links mapping events (e.g. `(click)`) to TS methods.
- Configure target project folder and SQLite connection in `src/HoangTLM.CodeBase.WebScanner/config.json`.
- Run:
  ```bash
  cd src/HoangTLM.CodeBase.WebScanner
  npm install
  npm start
  ```

### 4. Running the Web API and UI Dashboard
The backend Web API and Angular Frontend communicate to visualize and edit the catalog:
- Start C# Backend Web API (runs on `http://localhost:5080`):
  ```bash
  dotnet run --project src/HoangTLM.CodeBase.DatabaseScanner.Api/HoangTLM.CodeBase.DatabaseScanner.Api.csproj
  ```
- Start Angular Web Client:
  ```bash
  cd src/HoangTLM.CodeBase.DatabaseScanner.Web
  npm install
  npm start
  ```
- Open browser at `http://localhost:4200` to interact.

---

## Debugging in VS Code

We have pre-configured VS Code debug launch profiles. Open VS Code `Run and Debug` tab (`Cmd+Shift+D` or `Ctrl+Shift+D`) and choose:

- **`Debug Web API`**: Debugs the ASP.NET Core API backend.
- **`Debug Angular Web`**: Launches Chrome to debug the Angular frontend canvas.
- **`Debug .NET Scanner`**: Debugs the Roslyn C# code scanner console app.
- **`Debug Web Scanner`**: Debugs the Node.js TypeScript and HTML template scanner.
- **`Debug Database Scanner`**: Debugs the SQL Server schema scanner console app.
- **`Debug API + Web`** (Compound): Concurrently runs both the Web API and Chrome Angular Web Client for a seamless development experience.
