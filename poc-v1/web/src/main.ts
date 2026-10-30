import {Component,signal,provideZonelessChangeDetection} from '@angular/core';
import {bootstrapApplication} from '@angular/platform-browser';
import {FormArray,FormControl,FormGroup,ReactiveFormsModule,Validators} from '@angular/forms';
@Component({selector:'poc-grid',standalone:true,imports:[ReactiveFormsModule],template:`
 <main><h1>{{ar()?'تجربة جدول تقني':'Technical grid experiment'}}</h1>
 <button id="language" (click)="toggle()">{{ar()?'English':'العربية'}}</button>
 <button id="permission" (click)="viewCost.set(!viewCost())">{{ar()?'صلاحية التكلفة التجريبية':'Test cost visibility permission'}}</button>
 <p id="count">{{rows.length}} {{ar()?'سطر اختباري':'test rows'}}</p>
 <form [formGroup]="form"><table aria-label="Editable test lines"><thead><tr><th scope="col">#</th><th scope="col">{{ar()?'الصنف':'Product'}}</th><th scope="col">{{ar()?'الكمية':'Quantity'}}</th><th scope="col">{{ar()?'السعر':'Price'}}</th>@if(viewCost()){<th id="cost-head" scope="col">{{ar()?'التكلفة التجريبية':'Test cost'}}</th>}</tr></thead>
 <tbody formArrayName="rows">@for(row of rows.controls;track $index;let i=$index){<tr [formGroupName]="i"><th scope="row">{{i+1}}</th><td><input formControlName="product" [attr.aria-label]="'Product '+(i+1)" (keydown)="navigate($event,i,0)"></td><td><input formControlName="quantity" type="number" [attr.aria-label]="'Quantity '+(i+1)" [attr.aria-invalid]="row.controls.quantity.invalid" (keydown)="navigate($event,i,1)">@if(row.controls.quantity.invalid){<span role="alert">{{ar()?'كمية غير صحيحة':'Invalid quantity'}}</span>}</td><td><input formControlName="price" type="number" [attr.aria-label]="'Price '+(i+1)" (keydown)="navigate($event,i,2)"></td>@if(viewCost()){<td class="cost">{{i%7+1}} — fixture</td>}</tr>}</tbody></table>
 <button id="validate" type="submit" [disabled]="form.invalid">{{ar()?'تحقق':'Validate'}}</button></form>
 <p role="status" id="validity">{{form.valid?'VALID':'INVALID'}}</p></main>
 `,styles:[`main{font-family:Arial,sans-serif;max-width:1100px;margin:1rem auto}table{border-collapse:collapse;width:100%}th,td{border:1px solid #777;padding:.3rem;text-align:start}input{width:90%;padding:.3rem}button{margin:.4rem;padding:.5rem}input[aria-invalid=true]{outline:2px solid #b00}span[role=alert]{color:#b00}input:focus{outline:3px solid #05a}`]})
class Grid {
 ar=signal(true);viewCost=signal(false);
 rows=new FormArray(Array.from({length:100},(_,i)=>new FormGroup({product:new FormControl('صنف '+(i+1),{nonNullable:true,validators:[Validators.required]}),quantity:new FormControl(1,{nonNullable:true,validators:[Validators.required,Validators.min(0.00000001)]}),price:new FormControl(10,{nonNullable:true,validators:[Validators.required,Validators.min(0)]})})));
 form=new FormGroup({rows:this.rows});
 toggle(){this.ar.update(v=>!v);document.documentElement.lang=this.ar()?'ar':'en';document.documentElement.dir=this.ar()?'rtl':'ltr';}
 navigate(e:KeyboardEvent,row:number,col:number){if(e.key!=='ArrowDown'&&e.key!=='ArrowUp'&&e.key!=='Enter')return;e.preventDefault();const next=row+(e.key==='ArrowUp'?-1:1);if(next<0||next>=100)return;document.querySelectorAll<HTMLInputElement>('tbody input')[next*3+col]?.focus();}
}
bootstrapApplication(Grid,{providers:[provideZonelessChangeDetection()]}).catch(e=>{console.error(e);throw e;});
