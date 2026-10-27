# End-to-End RAG Architecture & Implementation Guide

> [!IMPORTANT]
> **Core Architectural Reference**
> This document defines the code-first RAG (Retrieval-Augmented Generation) pipeline for the project. Both Ingestion (Phase 1) and Retrieval (Phase 2) are implemented directly in C# without relying on black-box Azure portal indexers.

---

## Architecture Overview

```
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 1: INGESTION PIPELINE (When User Uploads a File)                 │
└────────────────────────────────────────────────────────────────────────┘
 [User PDF/Doc] ──> [1. Text Extraction] ──> [2. Text Chunking]
                                                    │
                                                    ▼
 [Azure AI Search Index] <── [4. Upsert] <── [3. Generate Embeddings]
 (Stores text + vectors + userId)            (text-embedding-3-small)

──────────────────────────────────────────────────────────────────────────

┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 2: RETRIEVAL & GENERATION (When User Asks a Question)            │
└────────────────────────────────────────────────────────────────────────┘
 [User: "What was Q3 profit?"]
              │
              ▼
   [1. Embed Question] ──> Vector: [0.014, -0.023, 0.891, ...]
              │
              ▼
   [2. Hybrid Search in Azure Search]
       ├── Vector Similarity (HNSW / Cosine)
       ├── Keyword Match (BM25)
       └── Filter: userId eq 'user_123' (Security Isolation)
              │
              ▼ (Top 3 most relevant chunks retrieved)
   [3. Augment Prompt]
       "You are an assistant. Use this context to answer:
        Context: [Chunk 1: 'Q3 net profit was $4.2M...']
        Question: What was Q3 profit?"
              │
              ▼
   [4. LLM Generation (GPT-4o)] ──> "Q3 profit was $4.2M."
```

---

## Implementation Details

### Phase 1: Ingestion Pipeline (In `FileService.cs`)
*Triggered immediately after a document is uploaded to Azure Blob Storage.*

1. **Text Extraction:**
   * **Library:** `PdfPig` (lightweight, open-source .NET PDF text reader, runs in-memory).
   * Extracts raw text page by page from the stream.

2. **Text Chunking:**
   * Slices extracted text into digestible segments (e.g., 500–1000 characters with 100–200 character overlap).
   * Overlap prevents loss of contextual meaning across chunk boundaries.

3. **Generate Embeddings:**
   * **Model:** Azure OpenAI `text-embedding-3-small` (1,536 dimensions).
   * Converts each text chunk into a dense numerical vector (`float[]`).

4. **Upsert to Azure AI Search:**
   * Uploads chunks with metadata:
     - `Id`: Unique chunk ID (`$"{documentId}_{chunkIndex}"`)
     - `DocumentId`: Foreign key to `UserDocument.Id`
     - `UserId`: Authenticated user ID (for security filter)
     - `Content`: Raw text of the chunk
     - `ContentVector`: 1,536-dimensional embedding vector
   * Updates `ChunkCount` in the PostgreSQL database.

---

### Phase 2: Retrieval & Generation (In `ChatController.cs`)
*Triggered when an authenticated user sends a prompt.*

1. **Embed User Question:**
   * Converts the user's prompt into an embedding vector using `text-embedding-3-small`.

2. **Hybrid Search with User Filtering:**
   * Queries Azure AI Search using:
     - **Vector Search:** HNSW / Cosine similarity against `ContentVector`.
     - **Keyword Search:** BM25 matching against `Content`.
     - **Security Filter:** `userId eq '{currentUserId}'` (prevents cross-tenant data leakage).
   * Selects Top-K (3 to 5) most relevant chunks.

3. **Prompt Augmentation:**
   * Combines retrieved chunks into system context.

4. **LLM Generation:**
   * Sends augmented prompt to Azure OpenAI `gpt-4o` / `gpt-4o-mini` to stream or return the final grounded answer.



#Important keywords used in functions or code: 
1. ChunkOverlap: chunkOverlap is the number of shared characters repeated between two consecutive text chunks  
   to keep the context intact across the split. it is like value of chunksize is duplicated between 2 consecutives chunks.
   chunk1 : 0 - 100 chars, chunk 2: 800-1800 chars


#Azure AI search
--Index: It is a blueprint or root container which contans field metadata, vectorsearch, SemanticSearch
--HnswAlgorithmConfiguration: Hierarchical Navigable Small World (HNSW), Constructs a multi-layer graph of vectors for fast approximate nearest-neighbor (ANN) lookups.
--Vectorizers: it is optional parameter. If passed it will allow azure to create vectors on the fly using azure open AI or any custom model

