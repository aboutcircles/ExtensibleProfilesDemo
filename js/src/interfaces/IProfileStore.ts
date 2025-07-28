/**
 * Interface for profile storage operations
 */
export interface IProfileStore {
  /**
   * Get a profile by avatar name
   * @param avatar - Avatar name
   * @returns Promise resolving to the profile data as a JSON object or null if not found
   */
  getProfileAsync(avatar: string): Promise<Record<string, any> | null>;
  
  /**
   * Update a profile for an avatar
   * @param avatar - Avatar name
   * @param profile - Profile data as a JSON object
   * @returns Promise resolving to true if update was successful, false otherwise
   */
  updateProfileAsync(avatar: string, profile: Record<string, any>): Promise<boolean>;
}
