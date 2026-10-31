'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { FormAlert } from '@/components/auth/form-alert';
import { FormField } from '@/components/auth/form-field';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { api } from '@/lib/api/client';
import { messageForError } from '@/lib/api/error-messages';
import {
  ACCEPTED_PHOTO_TYPES,
  createWorldSchema,
  type CreateWorldValues,
} from '@/lib/validation/world-schemas';

/**
 * Names a place and uploads its photo (US-010).
 *
 * The file input accepts the three types the API keeps and no `capture` attribute,
 * so a phone offers both the camera and the photo library (US-010 criterion 2).
 * What the learner picked is shown straight away, read from the file in the browser,
 * because a photo of the wrong room is easier to spot than to describe.
 *
 * Every grid here has one column of `minmax(0, 1fr)`. Safari on an iPhone draws a
 * file input wider than a phone's panel and will not shrink it, and an `auto`
 * column grows to fit it, taking the name field and the button past the edge
 * with it (WCAG 1.4.10).
 */
export function CreateWorldForm() {
  const router = useRouter();
  const [formError, setFormError] = useState<string | null>(null);
  const [preview, setPreview] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<CreateWorldValues>({ resolver: zodResolver(createWorldSchema) });

  // An object URL is a reference the browser keeps until it is told otherwise.
  useEffect(
    () => () => {
      if (preview) URL.revokeObjectURL(preview);
    },
    [preview],
  );

  const choosePhoto = (file: File | undefined) => {
    setValue('photo', file as File, { shouldValidate: Boolean(file) });
    setPreview((previous) => {
      if (previous) URL.revokeObjectURL(previous);
      return file ? URL.createObjectURL(file) : null;
    });
  };

  const onSubmit = handleSubmit(async ({ name, photo }) => {
    setFormError(null);
    try {
      const world = await api.createWorld(name, photo);
      router.push(`/worlds/${world.id}`);
      router.refresh();
    } catch (error) {
      setFormError(messageForError(error));
    }
  });

  return (
    <form onSubmit={onSubmit} noValidate>
      {formError ? <FormAlert>{formError}</FormAlert> : null}

      <div className="grid grid-cols-1 gap-5">
        <FormField
          id="name"
          label="ชื่อโลก"
          hint="เช่น ห้องครัวที่บ้าน หรือโต๊ะทำงาน"
          error={errors.name?.message}
          {...register('name')}
        />

        <div className="grid grid-cols-1 gap-1.5">
          <Label htmlFor="photo" className="font-semibold">
            รูปภาพ
          </Label>
          <input
            id="photo"
            type="file"
            accept={ACCEPTED_PHOTO_TYPES.join(',')}
            aria-invalid={errors.photo ? true : undefined}
            aria-describedby={errors.photo ? 'photo-error' : 'photo-hint'}
            className="w-full min-w-0 rounded-xl border border-input bg-input/30 p-3 text-sm file:mr-3 file:rounded-lg file:border-0 file:bg-muted file:px-3 file:py-1.5 file:text-sm file:font-semibold"
            onChange={(event) => choosePhoto(event.target.files?.[0])}
          />
          {errors.photo ? (
            <p
              id="photo-error"
              role="alert"
              className="text-xs text-destructive"
            >
              {errors.photo.message}
            </p>
          ) : (
            <p id="photo-hint" className="text-xs text-muted-foreground">
              ถ่ายใหม่หรือเลือกจากเครื่องก็ได้ รองรับ JPEG, PNG, WebP ไม่เกิน 10
              MB
            </p>
          )}

          {preview ? (
            // The learner's own photo, never uploaded yet: a plain <img> is right here.
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={preview}
              alt="รูปที่เลือก"
              className="mt-2 max-h-64 w-full rounded-2xl object-cover"
            />
          ) : null}
        </div>

        <Button type="submit" variant="cta" size="xl" disabled={isSubmitting}>
          {isSubmitting ? 'กำลังอัปโหลด…' : 'สร้างโลกนี้'}
        </Button>
      </div>
    </form>
  );
}
