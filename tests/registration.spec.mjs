import { test, expect } from '@playwright/test';
import pg from 'pg';
import fs from 'node:fs';
const secrets=JSON.parse(fs.readFileSync('.local/test-settings.json'));
const db=new pg.Client({connectionString:process.env.TEST_DATABASE_URL??'postgresql://postgres@127.0.0.1:55432/safetunnels_test'});
// Synthetic checksum-valid values, not customer banking data.
const bban='9990000000000000000000';
const mod97=s=>[...s].reduce((r,c)=>(r*10+Number(c))%97,0);
const iban='IR'+String(98-mod97(bban+'182700')).padStart(2,'0')+bban;
let seq=0;
test.beforeAll(async()=>{
  await db.connect();
  const name=(await db.query('SELECT current_database() AS name')).rows[0].name;
  if(name!=='safetunnels_test')throw Error('Tests require the dedicated safetunnels_test database.');
});
test.afterAll(async()=>db.end());
async function fill(page,count=1){
  await page.goto('/');
  await page.locator('[name="Input.FirstName"]').fill('آزمایش '+(++seq));
  await page.locator('[name="Input.LastName"]').fill('کاربر ساختگی');
  for(let i=0;i<count;i++){
    if(i)await page.locator('#add-account').click();
    await page.locator(`[name="Input.Accounts[${i}].BankName"]`).selectOption('سایر بانک‌ها');
    await page.locator(`[name="Input.Accounts[${i}].OtherBankName"]`).fill('بانک آزمایشی');
    await page.locator(`[name="Input.Accounts[${i}].IBAN"]`).fill(iban);
  }
}
async function data(page){return page.locator('form').evaluate(f=>Object.fromEntries(new FormData(f)));}
test('ثبت شبا، همراه خالی و حذف فیلدهای کارت و حساب',async({page})=>{
  await fill(page);
  await expect(page.locator('[name*=CardNumber], [name*=AccountNumber]')).toHaveCount(0);
  await page.locator('#submit-button').click();
  await expect(page).toHaveURL(/\/Success$/);
  await expect(page.locator('h1')).toHaveText('اطلاعات شما با موفقیت ثبت شد.');
  const code=await page.locator('.tracking-code').textContent();
  expect(code).toMatch(/^[A-F0-9]{32}$/);
  const result=await db.query('SELECT a."Mobile", b.* FROM "Applicants" a JOIN "BankAccounts" b ON b."ApplicantId"=a."Id" WHERE a."TrackingCode"=$1',[code]);
  expect(result.rowCount).toBe(1);expect(result.rows[0].Mobile).toBeNull();
  expect(result.rows[0].IBAN).toMatch(/^v1:local1:/);
  expect(result.rows[0].BankName).toBe('بانک آزمایشی');
  expect(result.rows[0]).not.toHaveProperty('CardLastFour');
  expect(result.rows[0]).not.toHaveProperty('AccountNumber');
  expect(JSON.stringify(result.rows)).not.toContain(iban);
});
test('ثبت چند حساب و نرمال‌سازی ارقام فارسی',async({page})=>{
  await fill(page,3);
  await page.locator('[name="Input.Mobile"]').fill('+۹۸۹۱۲۳۴۵۶۷۸۹');
  await page.locator('[name="Input.Accounts[0].IBAN"]').fill(iban.slice(2).replace(/\d/g,d=>'۰۱۲۳۴۵۶۷۸۹'[+d]));
  await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
  const code=await page.locator('.tracking-code').textContent();
  const r=await db.query('SELECT "Mobile",(SELECT count(*) FROM "BankAccounts" b WHERE b."ApplicantId"=a."Id") AS count FROM "Applicants" a WHERE "TrackingCode"=$1',[code]);
  expect(r.rows[0].count).toBe('3');expect(r.rows[0].Mobile).toBe('09123456789');
});
for(const [field,value] of [['IBAN','IR00000000000000000000000000'],['Mobile','123']]){
 test('رد '+field+' در مرورگر و سرور',async({page})=>{
  await fill(page);const name=field==='Mobile'?'Input.Mobile':`Input.Accounts[0].${field}`;
  await page.locator(`[name="${name}"]`).fill(value);await page.locator('#submit-button').click();
  await expect(page.locator(`[name="${name}"]`)).toHaveAttribute('aria-invalid','true');
  const payload=await data(page);const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
  const response=await page.request.post('/',{form:payload});expect(response.status()).toBe(200);expect(await response.text()).toContain('field-validation-error');
  expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before);
 });
}
test('افزودن، حذف، بازشماره‌گذاری و سقف ده حساب',async({page})=>{
 await fill(page,3);await page.locator('[data-account]').nth(1).locator('.remove-account').click();
 await expect(page.locator('[data-account]')).toHaveCount(2);
 await expect(page.locator('[name="Input.Accounts[1].BankName"]')).toHaveValue('سایر بانک‌ها');
 for(let i=2;i<10;i++)await page.locator('#add-account').click();
 await expect(page.locator('#add-account')).toBeDisabled();await expect(page.locator('[data-account]')).toHaveCount(10);
});
test('سرور ثبت بدون حساب و بیش از ده حساب را رد می‌کند',async({page})=>{
 await fill(page);const base=await data(page);const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
 const empty={...base};for(const k of Object.keys(empty))if(k.startsWith('Input.Accounts'))delete empty[k];
 const response=await page.request.post('/',{form:empty});expect(response.status()).toBe(200);
 const tooMany={...base};for(let i=1;i<11;i++)for(const f of ['BankName','OtherBankName','IBAN'])tooMany[`Input.Accounts[${i}].${f}`]=base[`Input.Accounts[0].${f}`];
 const over=await page.request.post('/',{form:tooMany});expect(over.status()).toBe(400);
 expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before);
});
test('ارسال هم‌زمان یک درخواست فقط یک ثبت ایجاد می‌کند',async({page})=>{
 await fill(page,2);const payload=await data(page);const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
 const responses=await Promise.all(Array.from({length:8},()=>page.request.post('/',{form:payload,maxRedirects:0})));
 responses.forEach(r=>expect(r.status()).toBe(302));
 expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before+1);
});
test('افراد هم‌نام با درخواست مستقل ثبت می‌شوند',async({page})=>{
 const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
 for(let i=0;i<2;i++){
  await fill(page);await page.locator('[name="Input.FirstName"]').fill('نام یکسان');
  await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
 }
 expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before+2);
});
test('فرم فقط به مبدأ خود درخواست می‌فرستد و نام قابل اجرای HTML نیست',async({page})=>{
 const requests=[];page.on('request',r=>requests.push(r.url()));
 await fill(page);await page.locator('[name="Input.FirstName"]').fill('<script>window.testXss=1</script>');
 const payload=await data(page);payload['Input.Accounts[0].IBAN']='IR00';
 const r=await page.request.post('/',{form:payload});expect(await r.text()).not.toContain('<script>window.testXss=1</script>');
 expect(requests.every(u=>new URL(u).origin===new URL(page.url()).origin)).toBe(true);
 expect(await page.evaluate(()=>window.testXss)).toBeUndefined();
});
test('خرابی ذخیره حساب کل تراکنش را rollback می‌کند و فرم حفظ می‌شود',async({page})=>{
 await db.query(`CREATE OR REPLACE FUNCTION test_reject_account() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'synthetic failure'; END $$ LANGUAGE plpgsql; CREATE TRIGGER test_rollback BEFORE INSERT ON "BankAccounts" FOR EACH ROW EXECUTE FUNCTION test_reject_account();`);
 try{
  await fill(page,2);const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
  await page.locator('#submit-button').click();await expect(page.locator('.error-summary')).toContainText('ثبت اطلاعات انجام نشد');
  await expect(page.locator('[name="Input.Accounts[0].IBAN"]')).toHaveValue(iban);
  expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before);
 }finally{await db.query('DROP TRIGGER test_rollback ON "BankAccounts"; DROP FUNCTION test_reject_account();');}
});
test('دسترسی مدیر، ماسک، خروجی CSV و خروج',async({page})=>{
 await page.goto('/admin');await expect(page).toHaveURL(/\/admin\/login/);
 await page.locator('[name="Email"]').fill(secrets.email);await page.locator('[name="Password"]').fill(secrets.password);
 await page.locator('button[type=submit]').click();await expect(page).toHaveURL(/\/admin$/);
 const html=await page.content();expect(html).not.toContain(iban);
 const downloadPromise=page.waitForEvent('download');await page.getByRole('button',{name:'خروجی CSV ماسک‌شده'}).click();
 const download=await downloadPromise;const csv=fs.readFileSync(await download.path(),'utf8');
 expect(csv).not.toContain(iban);expect(csv).toContain('••••');expect(csv).not.toContain('CardMasked');expect(csv).not.toContain('AccountMasked');
 await page.getByRole('button',{name:'خروج',exact:true}).click();await page.goto('/admin');await expect(page).toHaveURL(/\/admin\/login/);
});
test('قفل حساب مدیر بعد از پنج رمز نادرست',async({page})=>{
 try{
  for(let i=0;i<5;i++){
   await page.goto('/admin/login');const token=await page.locator('[name="__RequestVerificationToken"]').inputValue();
   const r=await page.request.post('/admin/login',{form:{Email:secrets.email,Password:'IncorrectPassword1!',__RequestVerificationToken:token},maxRedirects:0});expect(r.status()).toBe(200);
  }
  const user=(await db.query('SELECT "LockoutEnd" FROM "AspNetUsers" WHERE "Email"=$1',[secrets.email])).rows[0];expect(new Date(user.LockoutEnd).getTime()).toBeGreaterThan(Date.now());
 }finally{await db.query('UPDATE "AspNetUsers" SET "LockoutEnd"=NULL,"AccessFailedCount"=0 WHERE "Email"=$1',[secrets.email]);}
});
test('CSRF، هدرهای امنیت و عدم افشای بانکی در لاگ',async({page})=>{
 const r=await page.goto('/');expect(r.headers()['cache-control']).toContain('no-store');expect(r.headers()['x-robots-tag']).toBe('noindex, nofollow');expect(r.headers()['content-security-policy']).toContain("frame-ancestors 'none'");
 const response=await page.request.post('/',{form:{'Input.FirstName':'Test'}});expect(response.status()).toBe(400);
 for(const f of ['.local/app.stdout.log','.local/app.stderr.log'])if(fs.existsSync(f)){const log=fs.readFileSync(f,'utf8');expect(log).not.toContain(iban);}
});
test('موبایل: بدون اسکرول افقی، کنترل قابل لمس و ثبت',async({page})=>{
 await page.setViewportSize({width:390,height:844});await fill(page);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
 const box=await page.locator('#submit-button').boundingBox();expect(box.height).toBeGreaterThanOrEqual(44);
 await page.screenshot({path:'test-results/mobile.png',fullPage:true});
 await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
});

