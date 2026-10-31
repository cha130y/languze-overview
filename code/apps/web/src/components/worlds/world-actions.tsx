'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { FormField } from '@/components/auth/form-field';
import { Button } from '@/components/ui/button';
import { api, type World } from '@/lib/api/client';
import { messageForError } from '@/lib/api/error-messages';
import {
  renameWorldSchema,
  type RenameWorldValues,
} from '@/lib/validation/world-schemas';

/** Renaming and deleting one world (US-013, US-014). */
export function WorldActions({ world }: { world: World }) {
  const router = useRouter();
  const [renaming, setRenaming] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  /*
   * Each step replaces the buttons that opened it, so focus would fall to the
   * top of the page. Opening moves it into the step; closing gives it back to
   * the button that opened it (WCAG 2.4.3).
   */
  const [returnFocusTo, setReturnFocusTo] = useState<
    'rename' | 'delete' | null
  >(null);
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<RenameWorldValues>({
    resolver: zodResolver(renameWorldSchema),
    defaultValues: { name: world.name },
  });

  const rename = handleSubmit(async ({ name }) => {
    setError(null);
    try {
      await api.renameWorld(world.id, name);
      setRenaming(false);
      setReturnFocusTo('rename');
      router.refresh();
    } catch (caught) {
      setError(messageForError(caught));
    }
  });

  const remove = async () => {
    setDeleting(true);
    setError(null);
    try {
      await api.deleteWorld(world.id);
      router.push('/worlds');
      router.refresh();
    } catch (caught) {
      setError(messageForError(caught));
      setDeleting(false);
    }
  };

  return (
    <div className="grid gap-4">
      {renaming ? (
        <form onSubmit={rename} noValidate className="grid gap-3">
          <FormField
            id="name"
            label="ชื่อโลก"
            autoFocus
            error={errors.name?.message}
            {...register('name')}
          />
          <div className="grid gap-3 sm:flex">
            <Button
              type="submit"
              variant="cta"
              size="xl"
              className="sm:flex-1"
              disabled={isSubmitting}
            >
              {isSubmitting ? 'กำลังบันทึก…' : 'บันทึกชื่อใหม่'}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="xl"
              className="sm:flex-1"
              onClick={() => {
                setRenaming(false);
                setReturnFocusTo('rename');
                setError(null);
              }}
              disabled={isSubmitting}
            >
              ยกเลิก
            </Button>
          </div>
        </form>
      ) : confirmingDelete ? (
        <div className="grid gap-3">
          <p className="text-sm text-muted-foreground">
            การลบโลกนี้จะเอารูปภาพและคำศัพท์ของโลกนี้ออกอย่างถาวร
            รูปภาพจะถูกลบออกจากที่จัดเก็บภายใน 24 ชั่วโมง
            คำที่อยู่ในโลกอื่นด้วยจะยังเก็บความคืบหน้าไว้
            และแต้มรวมของคุณไม่ลดลง
          </p>
          <div className="grid gap-3 sm:flex">
            <Button
              type="button"
              variant="destructive"
              size="xl"
              className="sm:flex-1"
              onClick={() => void remove()}
              disabled={deleting}
            >
              {deleting ? 'กำลังลบ…' : 'ยืนยันลบโลกนี้'}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="xl"
              className="sm:flex-1"
              autoFocus
              onClick={() => {
                setConfirmingDelete(false);
                setReturnFocusTo('delete');
                setError(null);
              }}
              disabled={deleting}
            >
              ยกเลิก
            </Button>
          </div>
        </div>
      ) : (
        <div className="grid gap-3 sm:flex">
          <Button
            type="button"
            variant="outline"
            size="xl"
            className="sm:flex-1"
            autoFocus={returnFocusTo === 'rename'}
            onClick={() => setRenaming(true)}
          >
            เปลี่ยนชื่อ
          </Button>
          <Button
            type="button"
            variant="destructive"
            size="xl"
            className="sm:flex-1"
            autoFocus={returnFocusTo === 'delete'}
            onClick={() => setConfirmingDelete(true)}
          >
            ลบโลกนี้
          </Button>
        </div>
      )}

      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}
