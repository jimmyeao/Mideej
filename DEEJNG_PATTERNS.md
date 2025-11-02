# DeejNG Audio Session & VU Meter Management - Deep Analysis

## Executive Summary

DeejNG uses a sophisticated **multi-tier caching strategy** to manage audio sessions and high-frequency VU meter updates while maintaining proper resource cleanup. This document captures the key patterns and best practices for application to Mideej.

## Core Architecture Principles

### 1. Tiered Caching Strategy

DeejNG implements three levels of caching to balance performance and responsiveness:

**Level 1: Audio Device (2-second cache)**
- Cached because: Enumeration is expensive, device rarely changes
- Refresh trigger: Every 2 seconds OR on known changes

**Level 2: Session Collection (50ms cache)**
- Cached because: Getting all sessions is COM-heavy
- Refresh trigger: Every 50ms (between meter updates at 25ms)

**Level 3: Process Names (60-second cleanup)**
- Cached because: Process lookups require Process.GetProcessById
- Cleanup trigger: Every 60 seconds, removes dead processes
- Max size: 30 entries (trim excess when exceeded)

### 2. Session Identity Tracking

Multiple sessions can exist for one application (browser tabs, game instances).
DeejNG uses a compound key:
- `(sessionId, instanceId)` from NAudio's GetSessionIdentifier & GetSessionInstanceIdentifier
- Prevents duplicate tracking
- Enables proper multi-session app support

### 3. Automatic Cleanup Mechanisms

**Session Cache (AudioService):**
- Max 15 application groups cached
- LRU (Least-Recently-Used) eviction when exceeded
- Removes stale entries older than 2 minutes
- Removes entries with dead processes

**Process Cache (AudioUtilities):**
- Max 30 process entries cached
- Auto-cleanup every 60 seconds
- Removes entries for dead processes first
- Then arbitrarily trims oldest entries if needed

**Device Cache (DeviceCacheManager):**
- 5-second backoff period on failed devices
- Prevents repeated COM calls to disconnected devices
- Backoff cleared on successful operations

