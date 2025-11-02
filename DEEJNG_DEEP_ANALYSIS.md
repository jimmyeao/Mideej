# DeejNG Deep Analysis - Audio Sessions & VU Meters

## Executive Summary

This document details the findings from analyzing DeejNG's audio session management
and VU meter update architecture - patterns applicable to Mideej.

## Key Findings

### 1. Three-Tier Caching Strategy

**Tier 1: Audio Device (2-second cache)**
- Located in MainWindow.xaml.cs lines 2088-2093
- GetDefaultAudioEndpoint is expensive COM call
- Devices change rarely (headphone plugin is exception)
- Refresh interval: Every 2 seconds

**Tier 2: Session Collection (50ms cache)**
- Located in MainWindow.xaml.cs lines 2100-2109
- Getting all sessions from device is COM-heavy
- Meter updates every 25ms, sessions cached 50ms apart
- Responsive to new applications launching

**Tier 3: Process Names (30-entry cache, 60s cleanup)**
- Located in AudioUtilities.cs lines 40-120
- Process.GetProcessById() expensive, called 40x/sec at meter rate
- Auto-cleanup every 60 seconds removes dead process entries
- Max 30 entries enforced, LRU eviction if exceeded

