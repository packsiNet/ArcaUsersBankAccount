import { test, expect } from '@playwright/test';
test('انتخاب بانک غیرقابل تایپ و نمایش نام بانک دیگر',async({page})=>{
 await page.goto('/');const select=page.locator('[name="Input.Accounts[0].BankName"]');
 expect(await select.evaluate(e=>e.tagName)).toBe('SELECT');
 const other=page.locator('[name="Input.Accounts[0].OtherBankName"]');
 await expect(other).toBeHidden();await select.selectOption('سایر بانک‌ها');await expect(other).toBeVisible();
 await other.fill('بانک دلخواه');await select.selectOption('ملی');await expect(other).toBeHidden();await expect(other).toHaveValue('');
 await select.selectOption('سایر بانک‌ها');await page.locator('#submit-button').click();await expect(other).toHaveAttribute('aria-invalid','true');
 await page.locator('#add-account').click();await expect(page.locator('[name="Input.Accounts[1].OtherBankName"]')).toBeHidden();
 await page.locator('[name="Input.Accounts[1].BankName"]').selectOption('سایر بانک‌ها');await expect(page.locator('[name="Input.Accounts[1].OtherBankName"]')).toBeVisible();
});
test('نام بانک دیگر در سرور الزامی است و پس از خطا حفظ می‌شود',async({page})=>{
 await page.goto('/');await page.locator('[name="Input.FirstName"]').fill('آزمون');await page.locator('[name="Input.LastName"]').fill('انتخاب بانک');
 await page.locator('[name="Input.Accounts[0].BankName"]').selectOption('سایر بانک‌ها');
 await page.locator('[name="Input.Accounts[0].IBAN"]').fill('IR27999000000000000000000000');
 const payload=await page.locator('form').evaluate(f=>Object.fromEntries(new FormData(f)));
 const r=await page.request.post('/',{form:payload});expect(r.status()).toBe(200);expect(await r.text()).toContain('OtherBankName');
 await page.locator('[name="Input.Accounts[0].OtherBankName"]').fill('بانک دلخواه');await page.locator('[name="Input.Accounts[0].IBAN"]').fill('IR00');
 const p=await page.locator('form').evaluate(f=>Object.fromEntries(new FormData(f)));const invalid=await page.request.post('/',{form:p});
 await page.setContent(await invalid.text());await expect(page.locator('[name="Input.Accounts[0].BankName"]')).toHaveValue('سایر بانک‌ها');await expect(page.locator('[name="Input.Accounts[0].OtherBankName"]')).toHaveValue('بانک دلخواه');
});
