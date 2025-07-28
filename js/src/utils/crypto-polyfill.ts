/**
 * Polyfill for crypto.getRandomValues in Node.js environments.
 * This is needed because Node.js doesn't provide crypto.getRandomValues natively.
 */

// Only run this polyfill in Node.js environment
if (typeof window === 'undefined') {
  // Check if we're in a Node.js environment and crypto isn't already defined
  if (typeof crypto === 'undefined') {
    // Import Node's crypto module
    const nodeCrypto = require('crypto');
    
    // Define global crypto object if it doesn't exist
    Object.defineProperty(global, 'crypto', {
      value: {
        getRandomValues: (buffer: Uint8Array) => {
          // Fill the buffer with cryptographically strong random values
          return nodeCrypto.randomFillSync(buffer);
        }
      },
      configurable: true
    });
  }
  // If crypto is defined but doesn't have getRandomValues
  else if (typeof (crypto as any).getRandomValues !== 'function') {
    const nodeCrypto = require('crypto');
    
    // Add getRandomValues method
    (crypto as any).getRandomValues = (buffer: Uint8Array) => {
      return nodeCrypto.randomFillSync(buffer);
    };
  }
}

// Export nothing - this is just for side effects
export {};
