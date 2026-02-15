import { useState } from "react";
import type { Transaction } from "../../Models/Transaction";
import Box from "../Box/Box";

const AddTransaction = () => {
  const [form, setForm] = useState({
    isExpense: false,
    date: new Date().toISOString().split("T")[0],
    description: "",
    amount: 0.0,
    category: "",
    bankAccount: "",
    otherSideOfTransaction: "",
  });

  const onSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    alert(JSON.stringify(form, null, 2));
  };

  return (
    <Box header="Add Transaction">
      <form className="p-3 grid grid-cols-8 items-center place-items-center gap-3" onSubmit={onSubmit}>
        <div className="flex flex-col gap-1 mb-3 text-l font-medium text-gray-700">
          <label htmlFor="type">Type</label>
          <div className="text-xs">
            <div className="flex flex-row gap-1.5">
              <input type="radio" id="income" name="type" checked={!form.isExpense} onChange={() => setForm({ ...form, isExpense: false })}></input>
              <label htmlFor="income">Income</label>
            </div>
            <div className="flex flex-row gap-1.5">
              <input type="radio" id="expense" name="type" checked={form.isExpense} onChange={() => setForm({ ...form, isExpense: true })}></input>
              <label htmlFor="expense">Expense</label>
            </div>
          </div>
        </div>
        <div className="flex flex-col gap-2 mb-3">
          <label htmlFor="otherSideOfTransaction">
            {form.isExpense ? "to" : "from"}
          </label>
          <input
            autoFocus
            type="text"
            id="otherSideOfTransaction"
            placeholder="Company S.A."
            value={form.otherSideOfTransaction}
            onChange={(e) => setForm({ ...form, otherSideOfTransaction: e.target.value })}
            className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"></input>
        </div>
        <div className="mb-2 flex flex-col gap-2">
          <label htmlFor="description">
            Description
          </label>
          <input
            type="text"
            id="description"
            placeholder="electricity bill"
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"></input>
        </div>
        <div>
          <label htmlFor="date">
            Date
          </label>
          <input
            type="date"
            id="date"
            defaultValue={new Date().toISOString().split("T")[0]}
            value={form.date}
            onChange={(e) => setForm({ ...form, date: e.target.value })}
            className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:border-blue-500 block p-2.5"></input>
        </div>
        <div className="mb-2 flex flex-col gap-2">
          <label htmlFor="amount" >
            Amount
          </label>
          <input
            type="number"
            step="0.01"
            id="amount"
            placeholder="123.45 PLN"
            value={form.amount}
            onChange={(e) => setForm({ ...form, amount: parseFloat(e.target.value) })}
            className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:border-blue-500 block w-full p-2.5"></input>
        </div>
        <div className="mb-2 flex flex-col gap-2">
          <label htmlFor="category">
            Category
          </label>
          <select
            id="category"
            name="category"
            value={form.category}
            onChange={(e) => setForm({ ...form, category: e.target.value })}
            className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:border-blue-500 block w-full p-2.5">
            <option value={"groceries"}>Groceries</option>
            <option value={"rent"}>Rent</option>
            <option value={"bills"}>Bills</option>
            <option value={"otherHousehold"}>Other Household</option>
            <option value={"salary"}>Salary</option>
            <option value={"entertainment"}>Entertainment</option>
          </select>
        </div>
        <div className="mb-2 flex flex-col gap-2">
          <label htmlFor="bankAccount">
            Bank Account
          </label>
          <select
            id="bankAccount"
            name="bankAccount"
            value={form.bankAccount}
            onChange={(e) => setForm({ ...form, bankAccount: e.target.value })}
            className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:border-blue-500 block w-full p-2.5">
            <option value={"Account1"}>Account 1</option>
            <option value={"Account2"}>Account 2</option>
          </select>
        </div>
        <div className="w-20 mt-6">
          <button
            type="submit"
            className="w-full py-2 px-4 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
            Add
          </button>
        </div>
      </form>
    </Box>
  );
};

interface AddTransactionDTO {
  date: Date;
  description: string;
  amount: number;
  category: string;
  bankAccount: string;
  otherSideOfTransaction: string;
  isExpense: boolean;
}

export default AddTransaction;
