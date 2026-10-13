'use strict';
const bulkForm = document.querySelector('.bulk-inquiry-form');
if (bulkForm) {
  const status = bulkForm.querySelector('.bulk-inquiry-status');
  const summary = sessionStorage.getItem('bulk-inquiry-summary');
  if (summary) { status.textContent = summary; sessionStorage.removeItem('bulk-inquiry-summary'); }
  bulkForm.addEventListener('submit', async event => {
    event.preventDefault();
    const button = bulkForm.querySelector('button');
    if (button.disabled) return;
    button.disabled = true;
    button.textContent = 'در حال استعلام همه…';
    const rowButtons = [...document.querySelectorAll('.inquiry-form button')].filter(b => !b.disabled);
    rowButtons.forEach(b => { b.disabled = true; });
    let succeeded = 0, failed = 0, total = 0;
    const post = async (url, bankId) => {
      for (;;) {
        const body = new FormData(bulkForm);
        if (bankId) body.set('bankId', bankId);
        const response = await fetch(url, { method: 'POST', body, credentials: 'same-origin', headers: { Accept: 'application/json' } });
        if (response.redirected) throw new Error('نشست مدیریت پایان یافته است؛ دوباره وارد شوید.');
        if (response.status === 429) {
          status.textContent = 'محدودیت تعداد درخواست؛ ادامهٔ استعلام پس از یک دقیقه…';
          await new Promise(resolve => setTimeout(resolve, 60000));
          continue;
        }
        if (!response.ok) throw new Error('استعلام همه متوقف شد؛ نتایج موفق قبلی ذخیره شده‌اند.');
        return response.json();
      }
    };
    try {
      status.textContent = 'در حال دریافت فهرست شباهای استعلام‌نشده…';
      const { accountIds } = await post(bulkForm.action);
      total = accountIds.length;
      if (!total) { status.textContent = 'تمام شباها قبلاً استعلام شده‌اند.'; return; }
      for (const id of accountIds) {
        status.textContent = `در حال بررسی ${succeeded + failed + 1} از ${total} شبا؛ ثبت‌شده: ${succeeded}، ناموفق: ${failed}`;
        const result = await post(bulkForm.dataset.accountUrl, id);
        if (result.success) succeeded++; else failed++;
      }
      sessionStorage.setItem('bulk-inquiry-summary', `استعلام همه پایان یافت؛ ${succeeded} نتیجه ثبت شد و ${failed} استعلام ناموفق بود.`);
      location.reload();
    } catch (error) {
      status.textContent = `${error instanceof Error ? error.message : 'ارتباط برقرار نشد.'} (${succeeded} نتیجه ثبت شده، ${failed} ناموفق از ${total} شبا)`;
    } finally {
      button.disabled = false;
      button.textContent = 'استعلام همه';
      rowButtons.forEach(b => { b.disabled = false; });
    }
  });
}

document.querySelectorAll('.inquiry-form').forEach(form => {
  form.addEventListener('submit', async event => {
    event.preventDefault();
    const button = form.querySelector('button');
    if (button.disabled) return;
    const output = form.parentElement.querySelector('.inquiry-results');
    button.disabled = true;
    button.textContent = 'در حال استعلام…';
    output.textContent = 'در حال دریافت اطلاعات حساب‌ها…';
    let complete = false;
    let correct = false;
    try {
      const response = await fetch(form.action, {
        method: 'POST', body: new FormData(form), credentials: 'same-origin',
        headers: { Accept: 'application/json' }
      });
      if (response.redirected) throw new Error('نشست مدیریت پایان یافته است؛ دوباره وارد شوید.');
      if (!response.ok) throw new Error(response.status === 429
        ? 'تعداد درخواست‌ها زیاد است؛ یک دقیقه دیگر تلاش کنید.' : 'استعلام انجام نشد؛ دوباره تلاش کنید.');
      const data = await response.json();
      complete = data.complete === true;
      correct = complete && data.accounts.length > 0 && data.accounts.every(account =>
        account.result.correct === true);
      output.replaceChildren();
      const owners = form.closest('tr').querySelector('.account-owner-names');
      owners.replaceChildren();
      const names = new Set(data.accounts.filter(account => account.result.success &&
        account.result.nameMatches === false && account.result.ownerName).map(account => account.result.ownerName));
      for (const name of names) {
        const line = document.createElement('small');
        line.className = 'account-owner-name';
        line.textContent = `نام اصلی صاحب حساب: ${name}`;
        owners.append(line);
      }
      for (const account of data.accounts) {
        const card = document.createElement('div');
        const addLine = text => { const p = document.createElement('p'); p.textContent = text; card.append(p); };
        const result = account.result;
        if (result.success) {
          card.className = result.correct ? 'inquiry-correct' : 'inquiry-incorrect';
          card.setAttribute('aria-label', result.status);
          addLine(`بانک: ${result.bankName || account.registeredBank}`);
          addLine(`وضعیت حساب: ${result.active === true ? 'فعال' : result.active === false ? 'غیرفعال' : 'نامشخص'}`);
        } else {
          addLine(`بانک: ${account.registeredBank}`);
          addLine(result.message);
        }
        output.append(card);
      }
      if (!data.accounts.length) output.textContent = 'حسابی برای استعلام ثبت نشده است.';
    } catch (error) {
      output.textContent = error instanceof Error && error.message !== 'Failed to fetch'
        ? error.message : 'ارتباط برقرار نشد؛ دوباره تلاش کنید.';
    } finally {
      button.disabled = complete;
      button.textContent = correct ? 'اطلاعات صحیح است' : complete ? 'استعلام ثبت شده' : 'استعلام همه شباها';
    }
  });
});
