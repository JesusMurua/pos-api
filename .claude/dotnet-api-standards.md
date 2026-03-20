### Component Responsibilities

Each layer has specific responsibilities:

1. **API Layer**: Handles HTTP requests, routing, and API endpoints.
2. **Domain Layer**: Contains business models, enums, and domain-specific rules.
3. **Repository Layer**: Manages data access and persistence.
4. **Services Layer**: Implements business logic and orchestrates operations.# .NET API Coding Standards & Best Practices

## Table of Contents
- [Introduction](#introduction)
- [Technical Stack](#technical-stack)
- [Project Structure](#project-structure)
- [Controllers](#controllers)
- [Services](#services)
- [Models & Validation](#models--validation)
- [Error Handling](#error-handling)
- [Documentation](#documentation)
- [Performance Considerations](#performance-considerations)

## Introduction

This document outlines the coding standards and best practices for our .NET API projects. Following these guidelines will help maintain code consistency, readability, and quality across the application.

## Technical Stack

Our API projects are built with the following core technologies:

- .NET 9
- Entity Framework Core
- Repository Pattern with Unit of Work
- Swagger/OpenAPI for documentation

## Project Structure

### Layer Organization

Our API applications follow a clean architecture pattern with separation of concerns. The standard project structure is organized as follows:

```
├── API (Presentation Layer)
│   ├── Controllers
│   ├── Middleware
│   └── Program.cs
├── Domain (Business Layer)
│   ├── Dependencies
│   ├── Enums
│   ├── Exceptions
│   ├── Extensions
│   ├── Helpers
│   ├── Models
│   ├── PartialModels
│   └── Settings
├── Repository (Data Layer)
│   ├── Dependencies
│   ├── IRepository (Repository Interfaces)
│   ├── Repository (Repository Implementations)
│   ├── Utils
│   ├── ApplicationDbContext.cs
│   ├── GenericRepository.cs
│   ├── IGenericRepository.cs
│   ├── IUnitOfWork.cs
│   └── UnitOfWork.cs
└── Services (Service Layer)
    ├── Dependencies
    ├── Adapter
    ├── IService (Service Interfaces)
    └── Service (Service Implementations)
```

### Naming Conventions

We follow consistent naming conventions across all projects:

- **Files**: `PascalCase.cs` (e.g., `FacilityController.cs`, `FacilityService.cs`)
- **Classes**: PascalCase (e.g., `FacilityController`, `FacilityService`)
- **Interfaces**: Prefix with I (e.g., `IFacilityService`, `IUnitOfWork`)
- **Methods**: PascalCase (e.g., `GetAsync`, `CreateAsync`)
- **Properties**: PascalCase (e.g., `Name`, `FacilityCode`)
- **Private fields**: camelCase with underscore prefix (e.g., `_facilityService`, `_unitOfWork`)

### Repository Pattern

We use the Repository pattern with a Generic Repository and Unit of Work:

1. **IGenericRepository**: Generic interface for basic CRUD operations.

```csharp
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<T> AddAsync(T entity);
    void Update(T entity);
    void Delete(T entity);
    Task<IEnumerable<T>> GetAsync(
        Expression<Func<T, bool>>? filter = null,
        string? includeProperties = null);
    Task<PageData<T>> GetListPagedAsync(
        PageFilter pageFilter,
        Expression<Func<T, bool>>? filter = null);
}
```

2. **IUnitOfWork**: Interface for transaction management.

```csharp
public interface IUnitOfWork : IDisposable
{
    IFacilityRepository Facility { get; }
    IDepartmentRepository Department { get; }
    IRoomRepository Room { get; }
    // Other repositories...
    
    Task<int> SaveChangesAsync();
}
```

3. **Repository Implementations**: Concrete implementations of the repositories.

```csharp
public class FacilityRepository : GenericRepository<Facility>, IFacilityRepository
{
    public FacilityRepository(ApplicationDbContext context) : base(context)
    {
    }
    
    // Facility-specific methods
    public async Task<IEnumerable<Facility>> GetActiveWithDepartmentsAsync()
    {
        return await _context.Facilities
            .Where(f => f.Status)
            .Include(f => f.Departments)
            .ToListAsync();
    }
}
```

## Controllers

### Controller Structure

- Each controller should focus on a single resource
- Follow RESTful conventions for endpoint naming
- Use attribute routing
- Include appropriate HTTP method attributes
- Return appropriate status codes

```csharp
[Route("api/[controller]")]
[ApiController]
public class FacilityController : ControllerBase
{
    private readonly IFacilityService _facilityService;
    
    public FacilityController(IFacilityService facilityService)
    {
        _facilityService = facilityService;
    }

    /// <summary>
    /// Creates a new facility.
    /// </summary>
    /// <param name="facility">The facility data to create.</param>
    /// <returns>The ID of the created facility.</returns>
    /// <response code="200">Returns the ID of the created facility.</response>
    /// <response code="400">If the facility data is invalid.</response>
    [HttpPost("create")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(Facility facility)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            facility = await _facilityService.CreateAsync(facility);
            return Ok(new { id = facility.Id });
        }
        catch (Exception ex)
        {
            return BadRequest(ex);
        }
    }

    /// <summary>
    /// Retrieves a facility by its identifier.
    /// </summary>
    /// <param name="id">The ID of the facility to retrieve.</param>
    /// <returns>The requested facility.</returns>
    /// <response code="200">Returns the requested facility.</response>
    /// <response code="404">If the facility is not found.</response>
    /// <response code="400">If there was an error processing the request.</response>
    [HttpGet("getById/{id}")]
    [ProducesResponseType(typeof(Facility), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFacility(int id)
    {
        try
        {
            var facility = await _facilityService.GetAsync(id);
            return Ok(facility);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

### Exception Handling in Controllers

Handle exceptions consistently in controllers:

```csharp
[HttpGet("getById/{id}")]
public async Task<IActionResult> GetFacility(int id)
{
    try
    {
        var facility = await _facilityService.GetAsync(id);
        return Ok(facility);
    }
    catch (NotFoundException ex)
    {
        // Specific exception handling for not found
        return NotFound(ex.Message);
    }
    catch (ValidationException ex)
    {
        // Specific exception handling for validation errors
        return BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        // General exception handling
        // Consider logging the exception here
        return BadRequest(ex.Message);
    }
}
```

### API Endpoint Design

- Keep URLs resource-based (e.g., `/api/facilities`)
- Use HTTP methods appropriately:
  - GET: Retrieve resource(s)
  - POST: Create a resource
  - PUT: Update a resource completely
  - PATCH: Update a resource partially
  - DELETE: Remove a resource
- Include versioning strategy

### Controller Documentation

Document controllers with XML comments:

```csharp
/// <summary>
/// Controller for managing facilities.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class FacilityController : ControllerBase
{
    // Controller implementation
}
```

## Services

## Services

### Service Structure

Services should follow the interface-implementation pattern and be organized by domain entity:

```csharp
public interface IFacilityService
{
    Task<Facility> GetAsync(int id);
    Task<PageData<Facility>> GetBySearchPaginated(string? name, int[]? status, PageFilter filters);
    Task<Facility> CreateAsync(Facility facility);
    Task<Facility> UpdateAsync(int id, Facility facility);
    Task<bool> DeleteAsync(int id);
}

public class FacilityService : IFacilityService
{
    private readonly IUnitOfWork _unitOfWork;
    
    public FacilityService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }
    
    public async Task<Facility> GetAsync(int id)
    {
        // Implementation
    }
    
    // Other method implementations
}
```

### Code Organization

Use regions to organize code logically in service implementations:

```csharp
public class FacilityService : IFacilityService
{
    private readonly IUnitOfWork _unitOfWork;
    
    public FacilityService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }
    
    #region Public API Methods
    
    /// <summary>
    /// Retrieves a facility by its identifier with all related entities.
    /// </summary>
    /// <param name="id">The ID of the facility to retrieve.</param>
    /// <returns>The facility with all its related data.</returns>
    /// <exception cref="NotFoundException">Thrown when the facility with the specified ID is not found.</exception>
    public async Task<Facility> GetAsync(int id)
    {
        var includes = new string[]
        {
            "Departments",
            "Departments.Rooms"
        };
        
        var facility = await _unitOfWork.Facility.GetAsync(
            facility => facility.Id == id,
            string.Join(",", includes)
        );
        
        if (facility == null)
        {
            throw new NotFoundException($"Facility with id {id} not found");
        }
        
        return facility;
    }
    
    // Other public methods
    
    #endregion
    
    #region Private Helper Methods
    
    private async Task ProcessEntities(Facility facility)
    {
        // Implementation
    }
    
    // Other private methods
    
    #endregion
}
```

### Asynchronous Programming

- Use async/await consistently for I/O-bound operations
- Follow Task-based Asynchronous Pattern (TAP)
- Name async methods with "Async" suffix

```csharp
public async Task<Facility> GetAsync(int id)
{
    return await _unitOfWork.Facility.GetByIdAsync(id);
}
```

### Service Documentation

Document service interfaces and implementations:

```csharp
/// <summary>
/// Provides operations for managing facilities.
/// </summary>
public interface IFacilityService
{
    /// <summary>
    /// Retrieves a facility by its identifier.
    /// </summary>
    /// <param name="id">The ID of the facility to retrieve.</param>
    /// <returns>The facility with its related data.</returns>
    /// <exception cref="NotFoundException">Thrown when the facility is not found.</exception>
    Task<Facility> GetAsync(int id);
}
```

## Models & Validation

### Entity Models

- Use clear and descriptive property names
- Include appropriate data annotations
- Consider using partial classes for extending functionality

```csharp
// Main entity in Models folder
public partial class Facility
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;
    
    public string? FacilityCode { get; set; }
    
    public decimal SquareFeet { get; set; }
    
    public bool Status { get; set; }
    
    public int JobSiteId { get; set; }
    
    public int FacilityTypeId { get; set; }
    
    public virtual ICollection<Department>? Departments { get; set; }
}

// Extension in PartialModels folder
public partial class Facility
{
    [NotMapped]
    public int TotalDepartments => Departments?.Count ?? 0;
    
    [NotMapped]
    public int ActiveDepartments => Departments?.Count(d => d.Status) ?? 0;
}
```

### FluentValidation (Optional)

If using FluentValidation, separate validation rules from the model:

```csharp
public class FacilityValidator : AbstractValidator<Facility>
{
    public FacilityValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters");
        
        RuleFor(x => x.FacilityCode)
            .MaximumLength(50).WithMessage("Facility code cannot exceed 50 characters");
        
        RuleFor(x => x.SquareFeet)
            .GreaterThan(0).When(x => x.SquareFeet != 0)
            .WithMessage("Square feet must be greater than 0");
    }
}
```

## Error Handling

### Exception Types

Use specific exception types:

- `NotFoundException`: When a requested resource is not found
- `ValidationException`: When input validation fails
- `UnauthorizedException`: When a user lacks permissions
- `InternalServerException`: For unexpected errors

### Controller Exception Handling

Handle exceptions appropriately in controllers:

```csharp
[HttpGet("{id}")]
public async Task<IActionResult> GetFacility(int id)
{
    try
    {
        var facility = await _facilityService.GetAsync(id);
        return Ok(facility);
    }
    catch (NotFoundException ex)
    {
        return NotFound(ex.Message);
    }
    catch (Exception ex)
    {
        // Log the exception
        return StatusCode(500, "An error occurred while processing your request");
    }
}
```

## Documentation

### XML Documentation

Always document public APIs with XML comments:

```csharp
/// <summary>
/// Retrieves a paginated list of facilities based on search criteria.
/// </summary>
/// <param name="name">Optional name filter.</param>
/// <param name="status">Optional status filter array.</param>
/// <param name="filters">Pagination and filtering parameters.</param>
/// <returns>A paginated result containing matching facilities.</returns>
public async Task<PageData<Facility>> GetBySearchPaginated(string? name, int[]? status, PageFilter filters)
{
    // Implementation
}
```

## Performance Considerations

### Pagination

Always implement pagination for list endpoints:

```csharp
public async Task<PageData<Facility>> GetBySearchPaginated(PageFilter filters)
{
    return await _repository.GetPagedAsync(filters);
}
```

### Efficient Querying

- Use projections when only specific properties are needed
- Avoid N+1 query problems with proper includes
- Consider database indexing for frequently queried fields
