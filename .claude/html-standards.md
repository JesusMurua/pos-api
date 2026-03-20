# Angular HTML & PrimeNG Coding Standards

## Table of Contents
- [Introduction](#introduction)
- [Technical Stack](#technical-stack)
- [General HTML Structure](#general-html-structure)
- [Forms & Inputs](#forms--inputs)
- [Tables & Data Display](#tables--data-display)
- [Validation & Error Handling](#validation--error-handling)
- [Layout & Responsive Design](#layout--responsive-design)
- [PrimeNG Components](#primeng-components)
- [Accessibility Considerations](#accessibility-considerations)

## Introduction

This document outlines the HTML coding standards and best practices for our Angular project using PrimeNG. Following these guidelines will help maintain code consistency, readability, and quality across the application's templates.

## Technical Stack

The project uses the following frontend technologies:

- Angular: ^18.x
- PrimeNG: ^17.18.15
- HTML5, CSS3 and SCSS

## General HTML Structure

### Template Organization

- Keep templates organized in a logical hierarchy
- Use semantic HTML elements where appropriate
- Limit template complexity - extract reusable parts into components

### Naming Conventions

- Use kebab-case for HTML IDs and custom attributes
- Use camelCase for Angular directives and bindings
- Maintain consistent naming patterns across templates

### Comments

- Add comments for complex sections of HTML
- Comment the beginning and end of large template blocks

```html
<!-- Department Edit Section Begin -->
<div class="department-edit-section">
  <!-- Content -->
</div>
<!-- Department Edit Section End -->
```

### Order of Attributes

Maintain a consistent order of attributes for better readability and easier code reviews:

1. Angular structural directives (*ngIf, *ngFor)
2. Angular input/output properties and form bindings (formControlName, [formGroup])
3. Component-specific inputs ([control], [options])
4. HTML attributes (id, type, name)
5. Directives and component selectors (pInputText, pButton)
6. Styling (class, [ngClass], [style])
7. Event bindings ((click), (onChange))

**Incorrect attribute ordering:**
```html
<input
    pInputText
    type="text"
    class="form-control"
    id="facilityCode"
    formControlName="facilityCode"
    [ngClass]="{...}"
/>
```

**Correct attribute ordering:**
```html
<input
    formControlName="facilityCode"
    id="facilityCode"
    type="text"
    pInputText
    class="form-control"
    [ngClass]="{...}"
/>
```

## Forms & Inputs

### Form Structure

- Always wrap form elements in a `<form>` element with [formGroup]
- Group related form fields in container divs with descriptive class names
- Use consistent grid classes for layout

```html
<form [formGroup]="facilityForm">
  <div class="p-fluid p-formgrid grid">
    <div class="field col-12 lg:col-4 md:col-4 gap-2">
      <!-- Form field content -->
    </div>
  </div>
</form>
```

### Input Elements

- Always include labels for form controls
- Use htmlFor attribute to associate labels with inputs
- Add proper accessibility attributes (aria-* attributes)
- Apply consistent styling and class patterns
- Use helper methods for input validation logic

**Complex validation (avoid):**
```html
<div class="field col-12 lg:col-2 md:col-2 gap-2">
  <label htmlFor="facilityCode">Facility Code</label>
  <input
    formControlName="facilityCode"
    id="facilityCode"
    type="text"
    pInputText
    [ngClass]="{
      'ng-dirty ng-invalid':
        facilityFormControls.facilityCode.invalid &&
        facilityFormControls.facilityCode.touched
    }"
  />
  <app-print-control-error
    [control]="facilityFormControls.facilityCode"
  />
</div>
```

**Simplified validation (preferred):**
```html
<div class="field col-12 lg:col-2 md:col-2 gap-2">
  <label htmlFor="facilityCode">Facility Code</label>
  <input
    formControlName="facilityCode"
    id="facilityCode"
    type="text"
    pInputText
    [ngClass]="{'ng-dirty ng-invalid': isInvalidField('facilityCode')}"
  />
  <app-print-control-error
    [control]="facilityFormControls.facilityCode"
  />
</div>
```

## Tables & Data Display

### Table Setup

- Use PrimeNG's p-table component for data tables
- Configure proper column headers with sort indicators where needed
- Implement lazy loading for tables with large datasets
- Configure appropriate pagination options

```html
<p-table
  #dtTable
  [value]="items"
  [rows]="10"
  [paginator]="true"
  [rowsPerPageOptions]="[10, 20, 30]"
  [showCurrentPageReport]="true"
  [rowHover]="true"
  [lazy]="true"
  dataKey="id"
  [totalRecords]="totalRecords"
  (onLazyLoad)="getItems($event)"
  [loading]="loadingTable"
>
  <!-- Table templates go here -->
</p-table>
```

### Table Templates

- Use consistent templates for table header, body, and footer
- Keep column widths consistent between header and body templates
- Use pSortableColumn for sortable columns
- Move complex formatting logic to component methods

**Complex formatting in template (avoid):**
```html
<td>
  {{
    item.squareFeet
      | number
          : (item.squareFeet % 1 === 0
                ? "1.0-0"
                : "1.1-5")
  }}
</td>
```

**Simplified formatting (preferred):**
```html
<td>{{ item.squareFeet | number: getNumberFormat(item.squareFeet) }}</td>
```

With corresponding component method:
```typescript
getNumberFormat(value: number): string {
  return value % 1 === 0 ? "1.0-0" : "1.1-5";
}
```

**Standard table structure:**
```html
<ng-template pTemplate="header">
  <tr>
    <th style="min-width: 8rem">Action</th>
    <th pSortableColumn="name" style="min-width: 15rem">
      Name
      <p-sortIcon field="name" />
    </th>
    <!-- Additional columns -->
  </tr>
</ng-template>

<ng-template pTemplate="body" let-item>
  <tr>
    <td>
      <p-button
        icon="pi pi-eye"
        aria-label="View details"
        styleClass="p-button-rounded p-button-text"
        (click)="viewItem(item.id)"
      />
    </td>
    <td>{{ item.name }}</td>
    <!-- Additional columns -->
  </tr>
</ng-template>
```

### Table Actions

- Group related actions together
- Use consistent button styles for similar actions
- Provide clear visual cues for destructive actions
- Always include accessibility attributes for icon-only buttons

**Missing accessibility (avoid):**
```html
<td>
  <p-button
    icon="pi pi-eye"
    styleClass="p-button-rounded p-button