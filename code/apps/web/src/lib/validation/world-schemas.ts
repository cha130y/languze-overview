import { z } from 'zod';

/** 1–50 characters, the same rule the API applies (V3, FR-010, FR-016). */
export const worldNameSchema = z
  .string()
  .trim()
  .min(1, 'กรุณาตั้งชื่อโลกของคุณ')
  .max(50, 'ชื่อโลกต้องไม่เกิน 50 ตัวอักษร');

/** What the API accepts, checked here too so a mistake is caught before the upload. */
export const ACCEPTED_PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
export const MAX_PHOTO_BYTES = 10 * 1024 * 1024;

export const photoSchema = z
  .instanceof(File, { message: 'กรุณาเลือกรูปภาพ' })
  .refine((file) => file.size > 0, 'กรุณาเลือกรูปภาพ')
  .refine(
    (file) => file.size <= MAX_PHOTO_BYTES,
    'รูปภาพต้องมีขนาดไม่เกิน 10 MB',
  )
  /*
   * The browser reports the type from the file name, which is a hint rather than a
   * fact: the API decides from the content (FR-011). Checking here saves an upload
   * that would be refused, and never replaces the check on the server.
   */
  .refine(
    (file) => ACCEPTED_PHOTO_TYPES.includes(file.type),
    'รองรับเฉพาะรูปภาพแบบ JPEG, PNG หรือ WebP',
  );

export const createWorldSchema = z.object({
  name: worldNameSchema,
  photo: photoSchema,
});

export const renameWorldSchema = z.object({ name: worldNameSchema });

export type CreateWorldValues = z.infer<typeof createWorldSchema>;
export type RenameWorldValues = z.infer<typeof renameWorldSchema>;
