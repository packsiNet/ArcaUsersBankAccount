(() => {
  'use strict';
  const form = document.querySelector('#registration-form');
  if (!form) return;
  const accounts = document.querySelector('#accounts');
  const add = document.querySelector('#add-account');
  const submit = document.querySelector('#submit-button');
  const template = accounts.firstElementChild.cloneNode(true);
  const ordinals = ['اول', 'دوم', 'سوم', 'چهارم', 'پنجم', 'ششم', 'هفتم', 'هشتم', 'نهم', 'دهم'];
  const digits = s => s.replace(/[۰-۹٠-٩]/g, c => String(c.charCodeAt(0) >= 1776 ? c.charCodeAt(0) - 1776 : c.charCodeAt(0) - 1632));
  const number = s => digits(s).replace(/[\s-]/g, '');
  const iban = s => { const n = number(s).toUpperCase(); return n.startsWith('IR') ? n : 'IR' + n; };
  function mod97(s) {
    if (!/^IR[0-9]{24}$/.test(s)) return false;
    return [...s.slice(4)+'1827'+s.slice(2,4)].reduce((r,c)=>(r*10+Number(c))%97,0)===1;
  }
  function error(input, message) {
    input.setAttribute('aria-invalid', message ? 'true' : 'false');
    const span = input.parentElement.querySelector('.field-error');
    if (span) { span.textContent=message; span.id=input.id+'_error'; input.setAttribute('aria-describedby',span.id); }
    return !message;
  }
  function validate(input) {
    const val=input.value.trim(), field=input.name.split('.').pop(); let message='';
    if (field==='FirstName'||field==='LastName') { if(!val||val.length>100)message='این فیلد الزامی است و باید حداکثر ۱۰۰ نویسه باشد.'; }
    else if (field==='Mobile') { const n=number(val).replace(/^\+98/,'0');if(n&&!/^09[0-9]{9}$/.test(n))message='شماره همراه معتبر وارد کنید.'; }
    else if (field==='BankName') { if(!val||![...input.options].some(o=>o.value===val))message='بانک را از فهرست انتخاب کنید.'; }
    else if (field==='OtherBankName') { if(!input.disabled&&(!val||val.length>100))message='نام بانک مورد نظر را وارد کنید (حداکثر ۱۰۰ نویسه).'; }
    else if(field==='IBAN'&&!mod97(iban(val)))message='شماره شبای معتبر ایران وارد کنید.';
    return error(input,message);
  }
  function renumber() {
    [...accounts.children].forEach((card,i)=> {
      card.querySelector('[data-ordinal]').textContent=ordinals[i];
      card.querySelector('.remove-account').hidden=i===0;
      card.querySelectorAll('input,select').forEach(input=> {
        const field=input.name.split('.').pop();input.name='Input.Accounts['+i+'].'+field;input.id='Input_Accounts_'+i+'__'+field;
        input.parentElement.querySelector('label').htmlFor=input.id;
        const span=input.parentElement.querySelector('.field-error');span.dataset.valmsgFor=input.name;span.id=input.id+'_error';input.setAttribute('aria-describedby',span.id);
      });
    });
    add.disabled=accounts.children.length>=10;
    document.querySelector('#account-status').textContent=accounts.children.length.toLocaleString('fa-IR')+' حساب از ۱۰ حساب';
  }
  add.addEventListener('click',()=> {
    if(accounts.children.length>=10)return;
    const card=template.cloneNode(true);
    card.querySelectorAll('input,select').forEach(i=>{i.value='';i.classList.remove('input-validation-error');i.removeAttribute('aria-invalid');});
    card.querySelectorAll('.field-error').forEach(s=>s.textContent='');
    accounts.append(card);renumber();syncOtherBank(card);card.querySelector('select').focus();
  });
  accounts.addEventListener('click',e=>{const b=e.target.closest('.remove-account');if(b&&accounts.children.length>1){b.closest('[data-account]').remove();renumber();add.focus();}});
  function syncOtherBank(card) {
    const select=card.querySelector('[data-bank-select]'),field=card.querySelector('[data-other-bank]'),input=field.querySelector('input');
    const other=select.value==='سایر بانک‌ها';field.hidden=!other;input.disabled=!other;input.required=other;
    if(!other){input.value='';input.classList.remove('input-validation-error');error(input,'');}
  }
  form.addEventListener('change',e=>{if(e.target.matches('[data-bank-select]')){const card=e.target.closest('[data-account]');syncOtherBank(card);validate(e.target);if(e.target.value==='سایر بانک‌ها')card.querySelector('[data-other-bank] input').focus();}});
  form.addEventListener('focusout',e=>{if(e.target.matches('input:not([type=hidden]):not(:disabled),select'))validate(e.target);});
  form.addEventListener('input', e=> {
    const input=e.target, format=input.dataset.format;
    if(format==='iban') {
      const pos=input.selectionStart, count=number(input.value.slice(0,pos)).length;
      const n=number(input.value).toUpperCase();
      // Formatting preserves all characters so invalid input is never silently accepted.
      input.value=(n.match(/.{1,4}/g)||[]).join(' ');
      let p=0,c=0;while(p<input.value.length&&c<count){if(input.value[p]!==' ')c++;p++;}input.setSelectionRange(p,p);
    }
    if(input.getAttribute('aria-invalid')==='true')validate(input);
  });
  form.addEventListener('submit', e=> {
    if(submit.disabled){e.preventDefault();return;}
    let valid=true;form.querySelectorAll('input:not([type=hidden]):not(:disabled),select').forEach(i=>{if(!validate(i))valid=false;});
    if(accounts.children.length<1||accounts.children.length>10)valid=false;
    if(!valid){e.preventDefault();form.querySelector('[aria-invalid=true]')?.focus();return;}
    submit.disabled=true;submit.textContent='در حال ثبت اطلاعات…';document.querySelector('#submit-status').textContent='لطفاً تا پایان ثبت منتظر بمانید.';
  });
  window.addEventListener('pageshow',()=>{submit.disabled=false;submit.textContent='ثبت نهایی اطلاعات ←';});
  renumber();
  [...accounts.children].forEach(syncOtherBank);
})();
