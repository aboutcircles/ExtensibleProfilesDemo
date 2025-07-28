/**
 * Implementation of RFC 8785 compliant JSON Canonicalization Scheme (JCS)
 * Ensures consistent JSON serialization across different platforms
 */
export class CanonicalJson {
  /**
   * Canonicalizes a JSON object into a string representation
   * Follows RFC 8785 for consistent output across platforms
   * 
   * @param obj Object to be canonicalized
   * @returns Canonical string representation
   */
  public static canonicalise(obj: any): string {
    if (obj === undefined) {
      throw new Error("Cannot canonicalize undefined");
    }
    
    // Convert null and primitive types directly
    if (obj === null) return "null";
    if (typeof obj === "boolean") return obj ? "true" : "false";
    if (typeof obj === "string") return JSON.stringify(obj);
    if (typeof obj === "number") return this.formatNumber(obj);
    
    // Handle arrays - preserve order but canonicalize elements
    if (Array.isArray(obj)) {
      const items = obj.map(item => this.canonicalise(item));
      return "[" + items.join(",") + "]";
    }
    
    // Handle objects - sort keys and check for duplicates
    if (typeof obj === "object") {
      // Get all keys and sort them (strict lexical order per RFC 8785)
      const keys = Object.keys(obj).sort();
      
      // Check for duplicate keys (case insensitive)
      const lowerCaseKeys = new Map<string, string>();
      for (const key of keys) {
        const lower = key.toLowerCase();
        if (lowerCaseKeys.has(lower)) {
          throw new Error(`Duplicate property key detected: '${key}' conflicts with '${lowerCaseKeys.get(lower)}'`);
        }
        lowerCaseKeys.set(lower, key);
      }
      
      // Build object with sorted keys
      const entries = keys.map(key => {
        const value = this.canonicalise(obj[key]);
        return JSON.stringify(key) + ":" + value;
      });
      
      return "{" + entries.join(",") + "}";
    }
    
    throw new Error(`Unsupported type: ${typeof obj}`);
  }

  /**
   * Alias for canonicalise to maintain backward compatibility
   * with code that expects stringify method.
   * 
   * @param obj Object to stringify in canonical form
   * @returns Canonical JSON string
   */
  public static stringify(obj: any): string {
    return this.canonicalise(obj);
  }

  /**
   * Canonicalizes an object without including its signature property
   * Used for signature calculation to avoid circular dependency
   * 
   * @param obj Object to canonicalize, typically a CustomDataLink
   * @returns Canonical string representation
   */
  public static canonicaliseWithoutSignature(obj: any): string {
    // Create a shallow copy of the object
    const clone = { ...obj };
    
    // Remove the signature if present
    delete clone.signature;
    
    // Canonicalize the remaining object
    return this.canonicalise(clone);
  }

  /**
   * Formats a number according to RFC 8785
   * Uses the shortest form that preserves the exact value
   * 
   * @param num The number to format
   * @returns Canonical string representation of the number
   */
  private static formatNumber(num: number): string {
    if (!isFinite(num)) {
      throw new Error(`Cannot canonicalize non-finite number: ${num}`);
    }
    
    // Integer handling
    if (Number.isInteger(num) && Math.abs(num) < 9007199254740991) {
      return num.toString();
    }
    
    // For decimal numbers, find the shortest representation
    // that round-trips correctly (matches C# behavior)
    
    // Try different formats and pick the shortest one that preserves value
    let standardNotation = num.toString();
    
    // Fixed-point notation with trimmed zeros
    let fixedNotation = num.toFixed(16).replace(/\.?0+$/, '');
    if (fixedNotation.endsWith('.')) fixedNotation = fixedNotation.slice(0, -1);
    
    // Exponential notation - remove unnecessary + in exponent to match RFC 8785
    let expNotation = num.toExponential(16)
      .replace(/\.?0+e/, 'e')  // Remove trailing zeros in mantissa
      .replace('e+', 'e');     // Remove + in positive exponents
    
    if (expNotation.includes('.e')) expNotation = expNotation.replace('.e', 'e');
    
    // Choose the shortest representation
    let result = standardNotation;
    if (fixedNotation.length < result.length) result = fixedNotation;
    if (expNotation.length < result.length) result = expNotation;
    
    // Verify round-trip to ensure we preserved the exact value
    if (parseFloat(result) !== num) {
      // If round-trip fails, fall back to standard notation
      // but still clean it up for RFC 8785 compliance
      result = standardNotation.replace('e+', 'e');
    }

    return result;
  }
}