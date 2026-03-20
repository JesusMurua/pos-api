# Angular Coding Standards & Best Practices

## Table of Contents
- [Introduction](#introduction)
- [Technical Stack](#technical-stack)
- [General Coding Standards](#general-coding-standards)
- [Code Organization](#code-organization)
- [Observable Management](#observable-management)
- [Component Best Practices](#component-best-practices)
- [Services & API Communication](#services--api-communication)
- [Error Handling](#error-handling)
- [Data Structures and Collection Management](#data-structures-and-collection-management)

## Introduction

This document outlines the Angular coding standards and best practices for our project. Following these guidelines will help maintain code consistency, readability, and quality across the application.

## Technical Stack

The project is built with the following core technologies:

- Angular: ^18.x
- PrimeNG: ^17.18.15
- Angular CDK: ^18.2.13

These specific versions influence some of the coding patterns and best practices documented here.

## General Coding Standards

### Language 

All code, comments, and documentation must be written in English:

- Use English for class, method, property, and variable names
- Write all comments in English
- Create documentation (JSDoc, README, etc.) in English
- Use English for commit messages and pull request descriptions

Example:
```typescript
/**
 * Gets facilities with pagination and filters
 * @param event Lazy load event from the table
 */
getFacilities(event?: LazyLoadEvent) {
  // Implementation details
}
```

### Code Style

- Use TypeScript's strict mode
- Follow consistent indentation (2 or 4 spaces)
- Use semicolons to end statements
- Limit line length to improve readability (recommended: 100-120 characters)
- Use single quotes for strings
- Add trailing commas in multi-line object literals

## Code Organization

### Structure & Naming

- Use feature modules to organize related functionality.
- Follow consistent naming patterns:
  - Files: `feature-name.type.ts` (e.g., `employee-shift.service.ts`, `employee-shift-list.component.ts`)
  - Classes: PascalCase with type suffix (e.g., `EmployeeShiftService`, `EmployeeShiftListComponent`)
  - Methods: camelCase (e.g., `getEmployeeShifts()`, `clearFilters()`)
  - Properties: camelCase (e.g., `loadingTable`, `employeeShifts`)
  - ViewChild references: Use descriptive names with dt prefix for tables (e.g., `dtDepartments`, `dtRooms`)

### Class Inheritance & Models

- Use inheritance for models with common properties:
  - Base classes like `ResourceClass<T>` for reusable model functionality
  - Extend base classes for specific model types (e.g., `FacilityType extends ResourceClass<FacilityType>`)
  
- Use constructors with nullish coalescing for model initialization:
  ```typescript
  constructor(model?: Partial<FacilityType>) {
    super(model);
    this.id = model?.id ?? 0;
    this.displayName = model?.displayName ?? "";
    this.value = model?.value ?? "";
    this.description = model?.description ?? null;
    // ...
  }
  ```

### Service Base Classes

- Extend base service classes for common functionality:
  ```typescript
  export class FacilityService extends ResourceService<Facility> {
    constructor(http: HttpClient, @Inject(ENV_CONFIG) private config: EnvironmentConfig) {
      super(http, Facility, `${config.api.baseUrl}/facility`);
    }
  }
  ```

### Code Regions

Use descriptive regions to group logically related code blocks. Standard regions include:

```typescript
//#region Properties
// Component properties, ViewChild references
@ViewChild("dtDepartments") dtDepartments: Table;
facilityId: number | null = null;
facilityForm: FormGroup = new FormGroup({});
//#endregion

//#region Constructor & Lifecycle Hooks
constructor(
  private fb: FormBuilder,
  private facilityService: FacilityService
) { }

ngOnInit() {
  this.createForm();
  this.initializeFilters();
  this.loadReferenceData();
  
  // Handle routing
  this.handleRouteParameters();
  this.setupNavigationTracking();
}

ngOnDestroy() {
  // Cleanup code
}
//#endregion

//#region Setup, Navigation & Routing Methods
private handleRouteParameters(): void {
  // Route parameter handling code
}
//#endregion

//#region Facilities Methods
// Methods specifically related to Facility operations
//#endregion

//#region Department Methods
// Methods specifically related to Department operations
//#endregion

//#region Department Row Editing Methods
/**
 * Initializes row editing for a department
 */
onRowDepartmentEditInit(rowIndex: number | string, dept?: Department) {
  // Implementation
}
//#endregion

//#region Room Methods
// Methods specifically related to Room operations
//#endregion
```

### Documentation

Use JSDoc comments for methods and properties:

```typescript
/**
 * Gets employee shifts with pagination and filters
 * @param event Lazy load event from the table
 */
getEmployeeShifts(event?: LazyLoadEvent) {
  // Implementation
}
```

## Observable Management

### When to Use `subscribe()` vs Async Pipe

#### Use `subscribe()` When:

- Working with tables that use lazy loading (PrimeNG's onLazyLoad)
- Initializing component data from HTTP calls (one-time loads)
- Need to perform multiple operations on the emitted data
- Need to update multiple component properties with the result

```typescript
getEmployeeShifts(event?: LazyLoadEvent) {
  this.loadingTable = true;
  const fields = this.filters.getRawValue();
  
  this.employeeShiftService
    .getEmployeeShiftsWithPagination(fields, event)
    .subscribe({
      next: (result: PageData<EmployeeShift>) => {
        this.employeeShifts = result.data;
        this.totalRecords = result.rowsCount;
        this.loadingTable = false;
      },
      error: (error) => {
        console.error('Error loading shifts', error);
        this.loadingTable = false;
        this.messageService.add({
          severity: 'error', 
          summary: 'Error', 
          detail: 'Failed to load data'
        });
      }
    });
}
```

#### Use Async Pipe When:

- Working with simple data displays that don't require manipulation
- Need to react to changes in the observable's value automatically
- Want to avoid manual subscription management
- Component logic is primarily presentational

```typescript
// In component
roomTypes$: Observable<RoomType[]> = this.pickListService.getRoomTypes().pipe(
  catchError(error => {
    console.error('Error loading room types:', error);
    return of([]);
  })
);

// In template
<ng-container *ngIf="roomTypes$ | async as roomTypes">
  <!-- Use roomTypes here -->
</ng-container>
```

### Using `next`, `error`, and `complete` Callbacks

Prefer the object literal syntax for `subscribe()` when you need to handle errors:

```typescript
observable.subscribe({
  next: (value) => {
    // Handle successful result
  },
  error: (error) => {
    // Handle error
    console.error('Error:', error);
    this.messageService.add({
      severity: 'error',
      summary: 'Error',
      detail: 'An error occurred'
    });
  },
  complete: () => {
    // Optional: Handle completion (rarely needed for HTTP)
  }
});
```

### HTTP Subscription Management

- HTTP observables complete automatically after emitting a value or error.
- No need to manually unsubscribe from HTTP calls.
- For other types of observables (BehaviorSubject, interval, etc.), always unsubscribe.

```typescript
private destroy$ = new Subject<void>();

ngOnInit() {
  this.someService.getData()
    .pipe(takeUntil(this.destroy$))
    .subscribe(data => {
      // Handle data
    });
}

ngOnDestroy() {
  this.destroy$.next();
  this.destroy$.complete();
}
```

## Component Best Practices

### Complex Components Organization

For complex components that handle multiple features (like detail pages):
- Divide functionality into logical regions
- Use clear naming for related methods
- Keep UI editing/data handling methods together by feature
- Separate initialization, data loading, and UI interaction logic

Example:
```typescript
export class FacilityDetailComponent implements OnInit, OnDestroy {
  //#region Properties
  // Basic properties
  //#endregion
  
  //#region Constructor & Lifecycle Hooks
  ngOnInit() {
    this.createForm();
    this.initializeFilters();
    this.loadReferenceData();
    this.handleRouteParameters();
  }
  //#endregion
  
  //#region Setup Methods
  //#endregion
  
  //#region Facilities Methods
  //#endregion
  
  //#region Department Methods
  //#endregion
  
  //#region Department Row Editing Methods
  onRowDepartmentEditInit(rowIndex: number | string, dept?: Department) {}
  onRowDepartmentEditSave(rowIndex: number) {}
  onRowDepartmentEditCancel(rowIndex: number) {}
  //#endregion
  
  //#region Room Methods
  //#endregion
}
```

### Input Form Handling

Initialize forms in a dedicated method:

```typescript
private createForm() {
  this.facilityForm = this.fb.group({
    name: new FormControl('', Validators.required),
    status: new FormControl(true),
    // Additional fields
  });
}

private initializeFilters() {
  this.departmentsFilters = this.fb.group({
    name: new FormControl(''),
    status: new FormControl(),
  });
}
```

### Loading States

Always manage loading states for async operations:

```typescript
getEmployeeShifts() {
  this.loadingTable = true;
  
  this.service.getEmployeeShifts()
    .subscribe({
      next: (result) => {
        // Update data
        this.loadingTable = false;
      },
      error: () => {
        this.loadingTable = false;
      }
    });
}
```

## Services & API Communication

### Service Methods

Return Observables from service methods:

```typescript
getEmployeeShiftsWithPagination(
  fields: { name?: string; status?: { id: number }[] },
  event: LazyLoadEvent
): Observable<PageData<EmployeeShift>> {
  const payload = {
    // Prepare payload
  };

  return this.http.post<PageData<EmployeeShift>>(
    `${this.apiUrl}/GetEmployeeShiftsPaginated`,
    payload
  );
}
```

### Service Context & State Management

Use service methods to manage context or state when needed:

```typescript
export class FacilityService extends ResourceService<Facility> {
  private readonly STORAGE_KEY = 'facility_context';
  
  // Set context with specific parameters
  setContext(facilityId: number, departmentId: number, facilityName: string): void {
    // Implementation
  }
  
  // Get current context
  getContext(): { facilityId: number, departmentId: number, facilityName: string } | null {
    // Implementation
  }
  
  // Clear context when needed
  clearContext(): void {
    // Implementation
  }
}
```

### Type Safety

Use typed models for all HTTP responses:

```typescript
export interface PageData<T> {
  data: T[];
  rowsCount: number;
  totalPages: number;
  currentPage: number;
}
```

## Error Handling

### HTTP Error Handling

Use `catchError` for handling HTTP errors in services:

```typescript
getEmployeeShifts(): Observable<EmployeeShift[]> {
  return this.http.get<EmployeeShift[]>(`${this.apiUrl}/getAll`)
    .pipe(
      map((result) => result.map((i) => new EmployeeShift(i))),
      catchError((error: HttpErrorResponse) => {
        // Log error or transform it
        console.error('API Error:', error);
        return throwError(() => new Error('Failed to fetch employee shifts'));
      })
    );
}
```

### Component Error Handling

Handle errors in components with user-friendly messages:

```typescript
this.service.getEmployeeShifts().subscribe({
  next: (shifts) => this.shifts = shifts,
  error: (error) => {
    console.error('Error fetching shifts:', error);
    this.messageService.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Unable to load employee shifts'
    });
  }
});
```

## Data Structures and Collection Management

### Using TypeScript Maps for Lookups

Use Maps or Record objects for efficient lookups and data transformations:

```typescript
// Map for day code lookups
dayAbbreviationToFullName = {
  'mon': 'Monday',
  'tue': 'Tuesday',
  'wed': 'Wednesday',
  // ...
};

// Or with explicit typing
dayLabels: Record<string, string> = {
  'mon': 'Monday',
  'tue': 'Tuesday',
  // ...
};
```

### Managing Complex Collections

When working with complex data like departments or rooms:
- Use typed arrays for main data collections
- Use FormArray for editable collections
- Track editing states in separate structures

```typescript
// Department Data
departments: Department[] = [];
departmentsFormArray!: FormArray;
loadingDepartments = false;
editingDeptRowKeys: { [s: string]: boolean } = {};
clonedDepartments: { [s: string]: Department } = {};
selectedDepartment: Department | AbstractControl | null = null;
```