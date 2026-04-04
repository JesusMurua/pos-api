# Design Doc Prompts - Backend (.NET/C#)

## Overview

Este documento contiene dos prompts para el flujo de desarrollo enterprise:
1. **GENERATE**: Crear Design Doc desde un problema/requerimiento
2. **IMPLEMENT**: Generar código desde un Design Doc existente

---

## Prompt 1: Generate Backend Design Doc

```
You are a Microsoft MVP Senior Staff level architect specializing in .NET 9, C#, Entity Framework Core, and high-performance API design.

I need you to create a **Backend Design Document** for the following feature/problem. The document must be comprehensive enough that another developer (or AI) can implement it without additional clarification.

**CRITICAL RULES:**
- DO NOT include any code implementations
- DO NOT write actual C# code
- ONLY provide architecture, logic, specifications, and contracts
- Focus on the WHAT and WHY, not the HOW (code)

## Required Sections

### 1. Executive Summary
- Problem statement (2-3 sentences)
- Proposed solution (2-3 sentences)
- Expected outcome/impact

### 2. Current State Analysis
- Existing architecture involved
- Current pain points or limitations
- Performance baseline (if applicable)

### 3. Technical Requirements

#### 3.1 Functional Requirements
- List each requirement with ID (FR-001, FR-002, etc.)
- Include acceptance criteria for each

#### 3.2 Non-Functional Requirements
- Performance targets (response time, throughput)
- Scalability requirements (record counts, concurrent users)
- Security considerations

### 4. Architecture Design

#### 4.1 Component Overview
- Services involved (names and responsibilities)
- Repositories involved (names and responsibilities)
- External dependencies

#### 4.2 Data Flow
- Step-by-step flow description (numbered)
- Input → Processing → Output for each operation

#### 4.3 Database Schema Changes
- New tables (name, purpose, key columns conceptually)
- Modified tables (what changes and why)
- New indexes (columns and purpose)
- NO actual SQL or migrations

### 5. API Contract

#### 5.1 Endpoints
For each endpoint:
- HTTP Method + Route
- Purpose
- Request payload structure (field names, types, validations)
- Response payload structure (field names, types)
- Possible HTTP status codes and meanings

#### 5.2 Service Interface
For each service method:
- Method signature concept (name, parameters, return type)
- Business logic description (step-by-step)
- Validation rules
- Exception scenarios

### 6. Business Logic Specifications

#### 6.1 Core Algorithms
- Describe each algorithm in plain language
- Include decision trees or flowcharts (text-based)
- Specify formulas if applicable

#### 6.2 Validation Rules
- List all validation rules with IDs (VR-001, VR-002)
- Specify error messages for each

#### 6.3 Edge Cases
- List each edge case
- Expected behavior for each

### 7. Performance Optimization Strategy

#### 7.1 Query Optimization
- Projection strategies (which fields to load)
- Eager vs lazy loading decisions
- Batch processing approach

#### 7.2 Bulk Operations
- Batch sizes
- Transaction boundaries
- Concurrency handling

#### 7.3 Caching Strategy
- What to cache
- Cache invalidation triggers
- Cache duration

### 8. Error Handling Strategy
- Exception types to use
- Logging requirements
- User-facing error messages

### 9. Testing Requirements
- Unit test scenarios (list)
- Integration test scenarios (list)
- Performance test criteria

### 10. Implementation Phases
- Phase breakdown with deliverables
- Dependencies between phases
- Estimated complexity per phase (Low/Medium/High)

---

## My Feature/Problem:

[DESCRIBE YOUR FEATURE OR PROBLEM HERE]

## Existing Context:

[PROVIDE ANY RELEVANT CONTEXT: existing models, services, database schema, etc.]

## Constraints:

[LIST ANY CONSTRAINTS: deadlines, technology limitations, team size, etc.]
```

---

## Prompt 2: Implement from Backend Design Doc

```
You are a Microsoft MVP Senior Staff level developer specializing in .NET 9, C#, Entity Framework Core, clean architecture, repository pattern, and high-performance API design.

I have a **Backend Design Document** that specifies exactly what needs to be built. Your task is to implement the code following the specifications precisely.

**CRITICAL RULES:**
1. Follow the Design Doc specifications EXACTLY - do not add features not specified
2. Follow all coding standards from "dotnet-api-standards.md"
3. Write ALL code in English (classes, methods, variables, XML comments)
4. Use proper XML documentation for all public members
5. Implement proper error handling as specified
6. Optimize for performance as specified in the Design Doc
7. WAIT for confirmation before implementing each phase/component

## Implementation Process

### Step 1: Analysis
- Read the entire Design Doc
- Identify implementation phases
- List files that need to be created/modified
- Ask clarifying questions if specifications are ambiguous

### Step 2: Confirmation
- Present your implementation plan
- Wait for explicit approval before writing any code

### Step 3: Implementation (per phase)
- Implement ONLY what is approved
- Follow the exact specifications from the Design Doc
- Use regions to organize code (#region)
- Include comprehensive XML documentation

### Step 4: Summary
- List all files created/modified
- Note any deviations from Design Doc (with justification)
- Suggest next steps

## Code Quality Requirements

### Service Layer
- Async/await throughout
- Proper dependency injection
- Transaction management where specified
- Logging at INFO level for operations, DEBUG for details

### Repository Layer
- Use projections instead of entity loading where specified
- Implement bulk operations with BulkConfig
- Follow N+1 query prevention patterns

### Controller Layer
- Proper HTTP status codes
- Model validation
- Consistent error response format

## Design Document:

[PASTE YOUR DESIGN DOC HERE]

## Current Codebase Context:

[PROVIDE ANY EXISTING CODE THAT NEEDS TO BE EXTENDED OR MODIFIED]
```

---

## Usage Examples

### Example 1: New Feature
```
User: [Pastes Generate prompt + describes "Employee Scheduling Optimization" feature]
Claude: [Generates comprehensive Design Doc without code]
User: [Reviews, approves, then pastes Implement prompt + Design Doc]
Claude: [Implements code following specifications]
```

### Example 2: Bug Fix / Optimization
```
User: [Pastes Generate prompt + describes "ReloadByTeams performance issue"]
Claude: [Generates Design Doc with root cause analysis and solution architecture]
User: [Pastes Implement prompt + approved Design Doc]
Claude: [Implements optimized solution]
```

---

## Best Practices

1. **Always generate Design Doc first** - Even for "simple" features
2. **Review before implementation** - Catch architectural issues early
3. **Version your Design Docs** - Keep them updated as requirements change
4. **Reference in commits** - Link Design Doc in commit messages
5. **Use for code reviews** - Compare implementation against spec
